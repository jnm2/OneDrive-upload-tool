using Microsoft.Graph;
using Microsoft.Identity.Client;
using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace OneDriveUploadTool
{
    internal sealed class EagerRefreshAuthenticationProvider : IAuthenticationProvider, IAsyncDisposable
    {
        private readonly IPublicClientApplication publicClientApplication;
        private readonly ImmutableArray<string> scopes;
        private Task<AuthenticationResult> authenticationTask;
        private readonly Timer timer;

        public EagerRefreshAuthenticationProvider(
            IPublicClientApplication publicClientApplication,
            ImmutableArray<string> scopes)
        {
            this.publicClientApplication = publicClientApplication;
            this.scopes = scopes;

            timer = new Timer(OnTimerCallback, state: null, Timeout.Infinite, Timeout.Infinite);

            authenticationTask = AuthenticateAsync();
            InitialAuthenticationTask = authenticationTask;
        }

        public Task InitialAuthenticationTask { get; }

        public ValueTask DisposeAsync() => timer.DisposeAsync();

        private async Task<AuthenticationResult> AuthenticateAsync()
        {
            var previouslyAuthenticatedAccount = authenticationTask is { IsCompletedSuccessfully : true }
                ? authenticationTask.Result.Account
                : null;

            var result = await GetAuthenticationResultAsync(previouslyAuthenticatedAccount, CancellationToken.None).ConfigureAwait(false);

            timer.Change(result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);

            return result;
        }

        private void OnTimerCallback(object? state)
        {
            authenticationTask = AuthenticateAsync();
        }

        private async Task<AuthenticationResult> GetAuthenticationResultAsync(IAccount? previouslyAuthenticatedAccount, CancellationToken cancellationToken)
        {
            if (previouslyAuthenticatedAccount is { })
            {
                try
                {
                    return await publicClientApplication
                        .AcquireTokenSilent(scopes, previouslyAuthenticatedAccount)
                        .ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (MsalUiRequiredException)
                {
                }
            }

            return await publicClientApplication
                .AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task AuthenticateRequestAsync(HttpRequestMessage request)
        {
            var result = await authenticationTask.ConfigureAwait(false);

            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", result.AccessToken);
        }
    }
}
