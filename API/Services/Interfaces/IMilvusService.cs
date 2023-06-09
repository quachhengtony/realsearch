namespace Realsearch.API.Services.Interfaces;

using Grpc.Core;
using IO.Milvus.Grpc;

public interface IMilvusService : IDisposable
{
    AsyncUnaryCall<SearchResults> SearchAsync(SearchRequest request);
    AsyncUnaryCall<QueryResults> QueryAsync(QueryRequest request);
    // Other methods for interacting with the Milvus service
}
