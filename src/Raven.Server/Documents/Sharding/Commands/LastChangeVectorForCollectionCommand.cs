﻿using System.Net.Http;
using Raven.Client.Documents.Commands;
using Raven.Client.Http;
using Raven.Client.Json.Serialization;
using Sparrow.Json;

namespace Raven.Server.Documents.Sharding.Commands
{
    public class LastChangeVectorForCollectionCommand : RavenCommand<LastChangeVectorForCollectionResult>
    {
        private readonly string _collection;

        public LastChangeVectorForCollectionCommand(string collection)
        {
            _collection = collection;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}/collections/last-change-vector?collection={UrlEncode(_collection)}";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };

            return request;
        }

        public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
        {
            if (response == null)
            {
                Result = null;
                return;
            }
            Result = JsonDeserializationClient.LastChangeVectorForCollectionResult(response);
        }

        public override bool IsReadRequest => true;
    }
}
