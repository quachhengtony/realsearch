using Grpc.Core;
using Grpc.Net.Client;
using IO.Milvus.Grpc;
using Realsearch.API.Services.Interfaces;
using static IO.Milvus.Grpc.MilvusService;

namespace Realsearch.API.Services;

public class MilvusService : IMilvusService
{
    private static MilvusServiceClient _milvusServiceClient;

    public MilvusService(IConfiguration config)
    {
        _milvusServiceClient = new MilvusServiceClient(GrpcChannel.ForAddress(config.GetConnectionString("MilvusGrpc")));
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public AsyncUnaryCall<QueryResults> QueryAsync(QueryRequest request)
    {
        return _milvusServiceClient.QueryAsync(request);
    }

    public AsyncUnaryCall<SearchResults> SearchAsync(SearchRequest request)
    {
        return _milvusServiceClient.SearchAsync(request);
    }
}