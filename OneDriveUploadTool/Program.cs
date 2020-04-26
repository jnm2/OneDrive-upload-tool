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
                await UploadAsync(Path.GetFullPath(source), destination, cancellationToken);
            });

            await command.InvokeAsync(args);
        }

        public static async Task UploadAsync(string sourceDirectory, string destination, CancellationToken cancellationToken)
        {
            var ((client, itemRequestBuilderFactory), files) = await (
                GetClientAndItemRequestBuilderFactoryAsync(),
                Task.Run(() => new FileSystemEnumerable<EnumeratedFileData>(sourceDirectory, EnumeratedFileData.FromFileSystemEntry).ToImmutableArray(), cancellationToken));

            foreach (var file in files)
            {
                await UploadFileAsync(client, itemRequestBuilderFactory, sourceDirectory, file, cancellationToken);
            }

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
            CancellationToken cancellationToken)
        {
            await using var fileStream = System.IO.File.OpenRead(file.FullPath);

            var relativePath = Path.GetRelativePath(source, file.FullPath);
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
                return;
            }

            var provider = new ChunkedUploadProvider(session, client, fileStream);
            var success = false;
            try
            {
                var uploadRequests = provider.GetUploadChunkRequests();

                var exceptions = new List<Exception>();

                foreach (var request in uploadRequests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await provider.GetChunkRequestResponseAsync(request, exceptions);

                    if (result.UploadSucceeded)
                        success = true;
                }
            }
            finally
            {
                if (!success) await provider.DeleteSession();
            }

            if (!success)
                throw new NotImplementedException("Upload failed");
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
