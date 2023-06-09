namespace API.Contracts;

public class CreateCollectionRequest
{
    public string collection_name { get; set; }
    public CollectionSchema schema { get; set; }
}

public class CollectionSchema
{
    public bool autoID { get; set; }
    public string description { get; set; }
    public List<CollectionField> fields { get; set; }
    public string name { get; set; }
}

public class CollectionField
{
    public string name { get; set; }
    public string description { get; set; }
    public bool is_primary_key { get; set; }
    public bool autoID { get; set; }
    public int data_type { get; set; }
    public List<TypeParams> type_params { get; set; }
}

public class TypeParams
{
    public string key { get; set; }
    public string value { get; set; }
}