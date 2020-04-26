using Microsoft.Graph;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TaskTupleAwaiter;
using Techsola;

namespace OneDriveUploadTool
{
    internal static partial class Program
    {
        public static async Task Main(string[] args)
        {
            var command = new RootCommand("Uploads all files in the specified local folder to the specified OneDrive folder.")
            {
                new Argument<string>("source") { Description = "The path to the local folder." },
                new Argument<string>("destination") { Description = "The path to the OneDrive destination folder." },
            };

            command.Handler = CommandHandler.Create(async (string source, string destination, CancellationToken cancellationToken) =>
            {
                var progressRenderer = new WindowsStructuredReportConsoleRenderer();

                await UploadAsync(
                    Path.GetFullPath(source),
                    destination,
                    new Progress<StructuredReport>(progressRenderer.Render),
                    cancellationToken);
            });

            await command.InvokeAsync(args);
        }

        public static async Task UploadAsync(
            string sourceDirectory,
            string destination,
            IProgress<StructuredReport> progress,
            CancellationToken cancellationToken)
        {
            var structuredProgress = progress.Start("Logging in and enumerating files");

            var ((client, itemRequestBuilderFactory), files) = await (
                GetClientAndItemRequestBuilderFactoryAsync(),
                Task.Run(
                    () =>
                    {
                        var enumerable = new FileSystemEnumerable<EnumeratedFileData>(
                            sourceDirectory,
                            EnumeratedFileData.FromFileSystemEntry,
                            new EnumerationOptions
                            {
                                AttributesToSkip = FileAttributes.System,
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = false,
                            });

                        enumerable.ShouldIncludePredicate = ShouldInclude;

                        return enumerable.ToImmutableArray();

                        static bool ShouldInclude(ref FileSystemEntry entry) => !entry.IsDirectory;
                    },
                    cancellationToken));

            var totalFileSize = files.Sum(file => file.Length);
            structuredProgress.AddJobSize(1 + totalFileSize);
            structuredProgress.Next("Uploading files", totalFileSize);

            var queue = new AsyncParallelQueue<object?>(
                files.Select(async file =>
                {
                    await UploadFileAsync(client, itemRequestBuilderFactory, sourceDirectory, file, structuredProgress.CreateSubprogress(file.Length), cancellationToken);

                    return (object?)null;
                }),
                degreeOfParallelism: 10,
                cancellationToken);

            await queue.WaitAllAsync();

            structuredProgress.Complete();

            async Task<(GraphServiceClient Client, Func<string, IDriveItemRequestBuilder> ItemRequestBuilderFactory)> GetClientAndItemRequestBuilderFactoryAsync()
            {
                var token = await GetAuthenticationTokenAsync(cancellationToken);

                var client = new GraphServiceClient(new DelegateAuthenticationProvider(request =>
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
                    return Task.CompletedTask;
                }));

                var itemRequestBuilderFactory = await GetDestinationItemRequestBuilderAsync(client, destination, cancellationToken);

                return (client, itemRequestBuilderFactory);
            }
        }

        private static readonly IDictionary<string, object> UploadAdditionalData = ImmutableDictionary<string, object>.Empty
            .Add("@microsoft.graph.conflictBehavior", "fail");

        private static async Task UploadFileAsync(
            GraphServiceClient client,
            Func<string, IDriveItemRequestBuilder> itemRequestBuilderFactory,
            string source,
            EnumeratedFileData file,
            IProgress<StructuredReport> progress,
            CancellationToken cancellationToken)
        {
            var relativePath = Path.GetRelativePath(source, file.FullPath);
            var structuredProgress = progress.Start("Opening " + relativePath, initialJobSize: 0);
            try
            {
                await using var fileStream = System.IO.File.OpenRead(file.FullPath);

                structuredProgress.AddJobSize(1);
                structuredProgress.Next("Creating upload session for " + relativePath);

                UploadSession session;
                try
                {
                    session = await itemRequestBuilderFactory(relativePath).CreateUploadSession(new DriveItemUploadableProperties
                    {
                        AdditionalData = UploadAdditionalData,
                        FileSystemInfo = new Microsoft.Graph.FileSystemInfo
                        {
                            CreatedDateTime = file.CreationTimeUtc,
                            LastModifiedDateTime = file.LastWriteTimeUtc,
                            LastAccessedDateTime = file.LastAccessTimeUtc,
                        },
                    }).Request().PostAsync(cancellationToken);
                }
                catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    structuredProgress.Complete("Skipped because file already exists at the destination.");
                    return;
                }

                structuredProgress.AddJobSize(file.Length);

                // Use minimum maxChunkSize to keep progress reports moving
                var provider = new ChunkedUploadProvider(session, client, fileStream, maxChunkSize: 320 * 1024);
                var success = false;
                try
                {
                    while (true)
                    {
                        var uploadRequests = provider.GetUploadChunkRequests().ToList();
                        if (!uploadRequests.Any())
                            throw new NotImplementedException("Upload has not succeeded and no upload chunks were requested.");

                        var exceptions = new List<Exception>();

                        foreach (var request in uploadRequests)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            structuredProgress.Next("Uploading " + relativePath, request.RangeLength);
                            var result = await provider.GetChunkRequestResponseAsync(request, exceptions);

                            if (result.UploadSucceeded)
                                success = true;
                        }

                        if (success) break;

                        cancellationToken.ThrowIfCancellationRequested();
                        await provider.UpdateSessionStatusAsync();
                    }
                }
                finally
                {
                    if (!success) await provider.DeleteSession();
                }
            }
            finally
            {
                structuredProgress.Complete();
            }
        }

        private static async Task<string> GetAuthenticationTokenAsync(CancellationToken cancellationToken)
        {
            var application = PublicClientApplicationBuilder.Create("f398db46-8115-42ae-a5e3-09e3b691d1cf")
                  .WithRedirectUri("http://localhost")
                  .Build();

            var authenticationResult = await application.AcquireTokenInteractive(new[] { "files.readwrite.all" })
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(cancellationToken);

            return authenticationResult.AccessToken;
        }

        private static async Task<Func<string, IDriveItemRequestBuilder>> GetDestinationItemRequestBuilderAsync(
            GraphServiceClient client,
            string destination,
            CancellationToken cancellationToken)
        {
            var (firstSegment, rest) = SplitFirstSegment(destination);

            var filter = "name eq '" + firstSegment.Replace("'", "''") + "'";

            var (matchingSharedItems, driveRootItems) = await (
                client.Drive.SharedWithMe().Request().Filter(filter).Top(2).GetAsync(cancellationToken),
                client.Drive.Root.Children.Request().Filter(filter).Top(2).GetAsync(cancellationToken));

            var totalCount = matchingSharedItems.Count + driveRootItems.Count;
            if (totalCount > 1)
                throw new NotImplementedException($"More than one shared or root item named '{firstSegment}' was found.");

            if (matchingSharedItems.Concat(driveRootItems).SingleOrDefault() is { } rootItem)
            {
                var rootRequestBuilder = client
                    .Drives[rootItem.RemoteItem.ParentReference.DriveId]
                    .Items[rootItem.RemoteItem.Id];

                return childPath => rootRequestBuilder.ItemWithPath(rest + '/' + childPath);
            }

            return childPath => client.Drive.Root.ItemWithPath(destination + '/' + childPath);
        }

        private static readonly char[] SeparatorChars = { '/', '\\' };

        private static (string firstSegment, string? rest) SplitFirstSegment(string path)
        {
            var separatorIndex = path.IndexOfAny(SeparatorChars);
            return separatorIndex != -1
                ? (path[..separatorIndex], path[(separatorIndex + 1)..])
                : (path, null);
        }
    }
}
