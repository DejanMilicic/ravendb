using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Session.Loaders;
using Raven.Client.Documents.Session.Operations;

namespace Raven.Client.Documents.Session
{
    public partial class AsyncDocumentSession
    {
        /// <inheritdoc />
        public async Task<T> LoadAsync<T>(string id, CancellationToken token = default(CancellationToken))
        {
            var loadOperation = new LoadOperation(this);
            loadOperation.ById(id);

            var command = loadOperation.CreateRequest();
            if (command != null)
            {
                await RequestExecutor.ExecuteAsync(command, Context, SessionInfo, token).ConfigureAwait(false);
                loadOperation.SetResult(command.Result);
            }

            return loadOperation.GetDocument<T>();
        }

        public async Task<Dictionary<string, T>> LoadAsync<T>(IEnumerable<string> ids,
            CancellationToken token = default(CancellationToken))
        {
            var loadOperation = new LoadOperation(this);
            await LoadAsyncInternal(ids.ToArray(), null, loadOperation, token).ConfigureAwait(false);

            return loadOperation.GetDocuments<T>();
        }

        public async Task<T> LoadAsync<T>(string id, Action<IIncludeBuilder<T>> includes, CancellationToken token = default(CancellationToken))
        {
            var result = await LoadAsync(new[] {id}, includes, token).ConfigureAwait(false);
            return result.Values.FirstOrDefault();
        }

        public Task<Dictionary<string, T>> LoadAsync<T>(IEnumerable<string> ids, Action<IIncludeBuilder<T>> includes, CancellationToken token = default(CancellationToken))
        {
            if (includes == null)
                return LoadAsync<T>(ids, token);
            var includeBuilder = new IncludeBuilder<T>(Conventions);
            includes.Invoke(includeBuilder);

            return LoadAsyncInternal<T>(
                ids.ToArray(),
                includeBuilder.DocumentsToInclude?.ToArray(),
                includeBuilder.CountersToInclude?.ToArray(),
                includeBuilder.IncludeAllCounters,
                token);
        }

        public async Task<Dictionary<string, T>> LoadAsyncInternal<T>(string[] ids, string[] includes,
            CancellationToken token = new CancellationToken())
        {
            var loadOperation = new LoadOperation(this);
            loadOperation.ByIds(ids);
            loadOperation.WithIncludes(includes?.ToArray());

            var command = loadOperation.CreateRequest();
            if (command != null)
            {
                await RequestExecutor.ExecuteAsync(command, Context, SessionInfo, token).ConfigureAwait(false);
                loadOperation.SetResult(command.Result);
            }

            return loadOperation.GetDocuments<T>();
        }

        public async Task<Dictionary<string, T>> LoadAsyncInternal<T>(string[] ids, string[] includes,
            string[] counters, bool includeAllCounters, CancellationToken token = new CancellationToken())
        {
            var loadOperation = new LoadOperation(this);
            loadOperation.ByIds(ids);
            loadOperation.WithIncludes(includes?.ToArray());
            if (includeAllCounters)
            {
                loadOperation.WithAllCounters();
            }
            else
            {
                loadOperation.WithCounters(counters);
            }

            var command = loadOperation.CreateRequest();
            if (command != null)
            {
                await RequestExecutor.ExecuteAsync(command, Context, SessionInfo, token).ConfigureAwait(false);
                loadOperation.SetResult(command.Result);
            }

            return loadOperation.GetDocuments<T>();
        }

        public async Task<IEnumerable<T>> LoadStartingWithAsync<T>(string idPrefix, string matches = null, int start = 0,
            int pageSize = 25, string exclude = null,
            string startAfter = null, CancellationToken token = default(CancellationToken))
        {
            var operation = new LoadStartingWithOperation(this);
            await LoadStartingWithInternal(idPrefix, operation, null, matches, start,
                pageSize, exclude, startAfter, token).ConfigureAwait(false);

            return operation.GetDocuments<T>();
        }

        public async Task LoadStartingWithIntoStreamAsync(string idPrefix, Stream output, string matches = null, int start = 0,
            int pageSize = 25, string exclude = null, string startAfter = null, CancellationToken token = default(CancellationToken))
        {
            await LoadStartingWithInternal(idPrefix, new LoadStartingWithOperation(this), output, matches, start,
                pageSize, exclude,  startAfter, token).ConfigureAwait(false);
        }


        private async Task<GetDocumentsCommand> LoadStartingWithInternal(string idPrefix, LoadStartingWithOperation operation, Stream stream = null, string matches = null,
            int start = 0, int pageSize = 25, string exclude = null, 
            string startAfter = null, CancellationToken token = default(CancellationToken))
        {
            operation.WithStartWith(idPrefix, matches, start, pageSize, exclude, startAfter);

            var command = operation.CreateRequest();
            if (command != null)
            {
                await RequestExecutor.ExecuteAsync(command, Context, SessionInfo, token).ConfigureAwait(false);

                if (stream != null)
                    Context.Write(stream, command.Result.Results.Parent);
                else
                    operation.SetResult(command.Result);
            }

            return command;
        }

        private async Task LoadAsyncInternal(string[] ids, Stream stream, LoadOperation operation,
            CancellationToken token = default(CancellationToken))
        {
            operation.ByIds(ids);

            var command = operation.CreateRequest();
            if (command != null)
            {
                await RequestExecutor.ExecuteAsync(command, Context, SessionInfo, token).ConfigureAwait(false);

                if (stream != null)
                    Context.Write(stream, command.Result.Results.Parent);
                else
                    operation.SetResult(command.Result);
            }
        }

        public async Task LoadIntoStreamAsync(IEnumerable<string> ids, Stream output ,CancellationToken token = default(CancellationToken))
        {
            await LoadAsyncInternal(ids.ToArray(), output, new LoadOperation(this), token).ConfigureAwait(false);
        }
    }
}
