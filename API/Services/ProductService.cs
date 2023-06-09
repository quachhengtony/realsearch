namespace API.Services;

using Google.Protobuf;
using Grpc.Core;
using IO.Milvus.Grpc;
using System.Text;
using System.Text.Json;
using Realsearch.API.Services.Interfaces;
using Realsearch.API.Models;
using Accord.Math;
using Realsearch.API.Enums;

using API.Protos;

public class ProductService : API.Protos.ProductService.ProductServiceBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<ProductService> _logger;
    private readonly IMilvusService _milvus;

    public ProductService(ILogger<ProductService> logger, IMilvusService milvus, IConfiguration config)
    {
        _logger = logger;
        _milvus = milvus;
        _config = config;
    }

    public override async Task<GetProductsResponse> GetProducts(GetProductsRequest request, ServerCallContext context)
    {
        try
        {
            var searchQuery = request.SearchQuery.Trim();
            var userEmail = request.UserEmail.Trim();
            var page = (request.Page * int.Parse(_config["Milvus:TopK"] ?? "25")).ToString();

            if (string.IsNullOrEmpty(request.SearchQuery))
            {
                return await Task.FromResult(new GetProductsResponse
                {
                    ProductDto = { }
                });
            }

            string vector;
            float[] textFloats;
            float[] imageTextFloats;
            float[] floats;

            // Encode search query using different encoders depends on which search mode is used
            if (request.Mode != 0 && request.Mode == (int)EncoderModel.CLIP)
            {
                bool isImageQuery = IsBase64String(request.SearchQuery);
                if (isImageQuery)
                {
                    vector = await EncodeImage(searchQuery);
                }
                else
                {
                    vector = await EncodeText(searchQuery, EncoderModel.CLIP);
                }
                vector = vector.Substring(2, vector.Length - 4);
                imageTextFloats = vector.Split(",").Select(float.Parse).ToArray();
                floats = imageTextFloats;
            }
            else
            {
                vector = await EncodeText(searchQuery, EncoderModel.BERT);
                vector = vector.Substring(1, vector.Length - 2);
                textFloats = vector.Split(",").Select(float.Parse).ToArray();
                floats = textFloats;
            }

            var placeholderGroup = new PlaceholderGroup();
            var placeholderValue = new PlaceholderValue
            {
                Type = PlaceholderType.FloatVector,
                Tag = "$0"
            };

            using (var memoryStream = new MemoryStream(floats.ToList().Count * sizeof(float)))
            using (var binaryWriter = new BinaryWriter(memoryStream))
            {
                for (int i = 0; i < floats.ToList().Count; i++)
                    binaryWriter.Write(floats.ToList()[i]);

                memoryStream.Seek(0, SeekOrigin.Begin);
                placeholderValue.Values.Add(Google.Protobuf.ByteString.FromStream(memoryStream));
            }
            placeholderGroup.Placeholders.Add(placeholderValue);

            // Search different collections depends on the search mode
            SearchRequest searchRequest;
            if (request.Mode == (int)EncoderModel.CLIP)
            {
                // If the search mode is CLIP, then perform vector search on the product_image collection and get the product_id to query the product collection
                searchRequest = new SearchRequest
                {
                    CollectionName = $"{_config["Milvus:CollectionName"]}_image",
                    PartitionNames = { "_default" },
                    Dsl = "",
                    DslType = DslType.BoolExprV1,
                    PlaceholderGroup = placeholderGroup.ToByteString(),
                    OutputFields = { "product_id" },
                    SearchParams = {
                new KeyValuePair { Key = "anns_field", Value = "image_vector" },
                new KeyValuePair { Key = "topk", Value = _config["Milvus:TopK"] },
                new KeyValuePair { Key = "params", Value = "{\"nprobe\": 100}" },
                new KeyValuePair { Key = "metric_type", Value = "IP" },
                new KeyValuePair { Key = "round_decimal", Value = "-1" },
                new KeyValuePair { Key = "offset", Value = page },
                }
                };
            }
            else
            {
                // If the search mode is SBERT, then perform vector search in the product collection and get id, display_name, and image_url
                searchRequest = new SearchRequest
                {
                    CollectionName = _config["Milvus:CollectionName"],
                    PartitionNames = { "_default" },
                    Dsl = "",
                    DslType = DslType.BoolExprV1,
                    PlaceholderGroup = placeholderGroup.ToByteString(),
                    OutputFields = { "id", "display_name", "image_url" },
                    SearchParams = {
                new KeyValuePair { Key = "anns_field", Value = "text_vector" },
                new KeyValuePair { Key = "topk", Value = _config["Milvus:TopK"] },
                new KeyValuePair { Key = "params", Value = "{\"nprobe\": 100}" },
                new KeyValuePair { Key = "metric_type", Value = "IP" },
                new KeyValuePair { Key = "round_decimal", Value = "-1" },
                new KeyValuePair { Key = "offset", Value = page },
                }
                };
            }

            var response = await _milvus.SearchAsync(searchRequest);

            QueryResults queryResponse;
            Google.Protobuf.Collections.RepeatedField<long> idResults;
            Google.Protobuf.Collections.RepeatedField<string> displayNameResults;
            Google.Protobuf.Collections.RepeatedField<string> imageUrlResults;
            Google.Protobuf.Collections.RepeatedField<long> longResults;

            var getProductReponse = new GetProductsResponse();
            var productDto = new ProductDto();

            if (request.Mode == (int)EncoderModel.CLIP)
            {
                // If the search mode is CLIP, query the product collection and return the top hits based on product_id
                longResults = response.Results.FieldsData.Where((field) => field.FieldName == "product_id").First().Scalars.LongData.Data;
                long[] temp = longResults.ToArray();
                string[] stringArray = Array.ConvertAll(temp, x => x.ToString());

                var queryRequest = new QueryRequest
                {
                    CollectionName = _config["Milvus:CollectionName"],
                    OutputFields = { "display_name", "image_url" },
                    Expr = $"id in [{string.Join(", ", stringArray)}]",
                    PartitionNames = { "_default" },
                };

                queryResponse = await _milvus.QueryAsync(queryRequest);

                displayNameResults = queryResponse.FieldsData[0].Scalars.StringData.Data;
                imageUrlResults = queryResponse.FieldsData[1].Scalars.StringData.Data;

                for (int i = 0; i < displayNameResults.Count; i++)
                {
                    productDto = new ProductDto
                    {
                        Id = i,
                        ProductDescription = displayNameResults[i].ToString(),
                        ProductImageUrl = imageUrlResults[i].ToString()
                    };
                    getProductReponse.ProductDto.Add(productDto);
                }
            }
            else
            {
                // If the search mode is SBERT, return the top hits
                idResults = response.Results.FieldsData.Where((d) => d.FieldName == "id").First().Scalars.LongData.Data;
                displayNameResults = response.Results.FieldsData.Where((d) => d.FieldName == "display_name").First().Scalars.StringData.Data;
                imageUrlResults = response.Results.FieldsData.Where((d) => d.FieldName == "image_url").First().Scalars.StringData.Data;


                if (displayNameResults != null)

                    for (int i = 0; i < displayNameResults.Count; i++)
                    {
                        productDto = new ProductDto
                        {
                            Id = (int)idResults[i],
                            ProductDescription = displayNameResults[i].ToString(),
                            ProductImageUrl = imageUrlResults[i].ToString()
                        };
                        getProductReponse.ProductDto.Add(productDto);
                    }
            }

            // Re-ranking search results
            queryResponse = await CheckUserProductPreference(userEmail);
            var userProductPreferenceResult = queryResponse?.FieldsData[0]?.Scalars?.StringData?.Data;

            if (userProductPreferenceResult != null && userProductPreferenceResult.ToString() != "[ ]")
            {
                _logger.LogInformation("User product preference found.");

                var preferences = new Dictionary<string, string>();
                foreach (var fieldData in queryResponse.FieldsData)
                {
                    if (fieldData.FieldName == "id")
                        continue;

                    var data = fieldData?.Scalars?.StringData?.Data[0];

                    if (data == null)
                        continue;

                    JsonDocument jsonDocument = JsonDocument.Parse(data);
                    JsonElement jsonElement = jsonDocument.RootElement[0];

                    preferences[fieldData.FieldName] = jsonElement.ToString();
                }

                Dictionary<string, Dictionary<string, string>> preferredAttributes = new();
                var temp = new Dictionary<string, string>();

                foreach (var pair in preferences)
                {
                    foreach (var property in JsonDocument.Parse(pair.Value).RootElement.EnumerateObject())
                    {
                        if (!preferredAttributes.ContainsKey(pair.Key))
                        {
                            temp = new Dictionary<string, string>();
                            temp.Add(property.Name, property.Value.ToString());
                            preferredAttributes.Add(pair.Key, temp);
                        }
                        else
                        {
                            if (int.Parse(property.Value.ToString()) > int.Parse(preferredAttributes[pair.Key].First().Value))
                            {
                                temp = new Dictionary<string, string>();
                                temp.Add(property.Name, property.Value.ToString());
                                preferredAttributes[pair.Key] = temp;
                            }
                        }
                    }
                }

                List<double[]> productVectors = new();
                List<double[]> preferenceVectors = new();
                double[] productVector;

                foreach (var preference in preferredAttributes)
                {
                    vector = await EncodeText(preference.Value.First().Key, EncoderModel.BERT);
                    vector = vector.Substring(1, vector.Length - 2);
                    preferenceVectors.Add(vector.Split(",").Select(double.Parse).ToArray());
                }

                for (int i = 0; i < getProductReponse.ProductDto.Count; i++)
                {
                    vector = await EncodeText(getProductReponse.ProductDto[i].ProductDescription, EncoderModel.BERT);
                    vector = vector.Substring(1, vector.Length - 2);
                    productVector = vector.Split(",").Select(double.Parse).ToArray();

                    double meanSim = 0;
                    foreach (var preferenceVector in preferenceVectors)
                    {
                        meanSim = meanSim + (1 - Distance.Cosine(preferenceVector, productVector));
                    }
                    getProductReponse.ProductDto[i].Score = (float)(meanSim / preferenceVectors.Count);
                }

                var sortedGetProductsResponse = new GetProductsResponse();
                var sortedProductDto = getProductReponse.ProductDto.OrderByDescending(x => x.Score);

                foreach (var product in sortedProductDto)
                {
                    sortedGetProductsResponse.ProductDto.Add(product);
                }
                return await Task.FromResult(sortedGetProductsResponse);
            }
            return await Task.FromResult(getProductReponse);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception: {ex.Message}");
            _logger.LogError(ex.StackTrace);
            return await Task.FromResult(new GetProductsResponse
            {
                ProductDto = { }
            });
        }
    }

    public override async Task<ActionStatusReponse> BuyProducts(BuyProductsRequest request, ServerCallContext context)
    {
        try
        {
            var buyerEmail = request.BuyerEmail.Trim();

            if (request.Id == null || request.Id.Count == 0 || string.IsNullOrEmpty(buyerEmail))
            {
                return await Task.FromResult(new ActionStatusReponse
                {
                    Status = "Failed"
                });
            }

            var queryRequest = new QueryRequest
            {
                CollectionName = _config["Milvus:CollectionName"],
                OutputFields = { "text_vector" },
                Expr = $"id in [{string.Join(", ", request.Id)}]",
                PartitionNames = { "_default" },
            };

            var queryRequest2 = new QueryRequest
            {
                CollectionName = _config["Milvus:CollectionName"],
                OutputFields = { "base_color", "season", "gender", "usage" },
                Expr = $"id in [{string.Join(", ", request.Id)}]",
                PartitionNames = { "_default" },
            };

            var queryResponse = await _milvus.QueryAsync(queryRequest);
            if (queryResponse?.FieldsData[0]?.Vectors?.FloatVector?.Data?.ToString() == "[ ]")
            {
                throw new Exception("Failed to find products with the given ids.");
            }

            _logger.LogInformation("Products with the given ids found.");

            var userProductPreferences = new List<UserProductPreference>();
            List<float[]> textVectors = new();
            ArraySegment<float> tempFloats;
            int vectorCount = queryResponse!.FieldsData[0].Vectors.FloatVector.Data.Count / 384;
            for (int i = 0; i < (384 * vectorCount); i += 384)
            {
                tempFloats = new ArraySegment<float>(queryResponse.FieldsData[0].Vectors.FloatVector.Data.ToArray(), i, 384);
                textVectors.Add(tempFloats.ToArray());
            }

            foreach (var vector in textVectors)
            {
                userProductPreferences.Add(new UserProductPreference
                {
                    BuyerEmail = buyerEmail,
                    ProductColor = "",
                    ProductGender = "",
                    ProductSeason = "",
                    ProductUsage = "",
                    ProductTextVector = vector
                });
            }
            // var textVectors = queryResponse.FieldsData[0].Vectors.FloatVector.Data.ToArray();

            queryResponse = await _milvus.QueryAsync(queryRequest2);
            if (queryResponse?.FieldsData[0]?.Scalars?.StringData?.Data?.ToString() == "[ ]")
            {
                throw new Exception("(2) Failed to find products with the given ids.");
            }
            else
            {
                _logger.LogInformation("(2) Products with the given ids found.");
            }

            Dictionary<string, List<Dictionary<string, string>>> dictionary = BuildDictionary(queryResponse);

            queryResponse = await CheckUserProductPreference(buyerEmail);

            var userProductPreferenceResult = queryResponse?.FieldsData[0]?.Scalars?.StringData?.Data;

            if (userProductPreferenceResult != null && userProductPreferenceResult.ToString() == "[ ]")
            {
                _logger.LogInformation("Failed to find existing user product preference.");
                var userProductPreference = new UserProductPreference
                {
                    BuyerEmail = buyerEmail,
                    ProductColor = JsonSerializer.Serialize(dictionary["base_color"]),
                    ProductGender = JsonSerializer.Serialize(dictionary["gender"]),
                    ProductSeason = JsonSerializer.Serialize(dictionary["season"]),
                    ProductUsage = JsonSerializer.Serialize(dictionary["usage"]),
                    ProductTextVector = textVectors[textVectors.Count - 1]
                };
                await CreateUserProductPreference(userProductPreference);
            }
            else
            {
                _logger.LogInformation("User product preference found.");
                Dictionary<string, string> preferences = new();

                var existingPreferenceResponse = queryResponse;

                if (existingPreferenceResponse?.FieldsData[0]?.Scalars?.StringData?.Data?.ToString() == "[ ]")
                {
                    throw new Exception("Failed to get product user preference.");
                }

                _logger.LogInformation("Get product user preference successfully.");
                foreach (var fieldData in existingPreferenceResponse.FieldsData)
                {
                    if (fieldData.FieldName == "id")
                        continue;

                    var data = fieldData?.Scalars?.StringData?.Data[0];

                    if (data == null)
                        continue;

                    JsonDocument jsonDocument = JsonDocument.Parse(data);

                    JsonElement jsonElement = jsonDocument.RootElement[0];

                    preferences[fieldData.FieldName] = jsonElement.ToString();
                }

                foreach (var key in dictionary.Keys)
                {
                    foreach (var item in dictionary[key][0])
                    {
                        string altKey, replacementKey, replacementValue;

                        if (key.Contains("color"))
                        {
                            altKey = "product_color";
                        }
                        else
                        {
                            altKey = $"product_{key}";
                        }

                        var same = preferences[altKey];
                        JsonElement jsonElement = JsonDocument.Parse(same).RootElement;
                        foreach (JsonProperty property in jsonElement.EnumerateObject())
                        {
                            string propertyName = property.Name;
                            JsonElement propertyValue = property.Value;
                            if (propertyName == item.Key)
                            {
                                replacementKey = propertyName;
                                replacementValue = propertyValue.ToString();
                                dictionary[key][0][item.Key] = Convert.ToString(int.Parse(dictionary[key][0][item.Key]) + int.Parse(replacementValue));
                            }
                        }
                    }
                }

                var mergedDictionary = MergeDictionaries(preferences, dictionary);

                var userProductPreference = new UserProductPreference
                {
                    BuyerEmail = buyerEmail,
                    ProductColor = JsonSerializer.Serialize(mergedDictionary["product_color"]),
                    ProductGender = JsonSerializer.Serialize(mergedDictionary["product_gender"]),
                    ProductSeason = JsonSerializer.Serialize(mergedDictionary["product_season"]),
                    ProductUsage = JsonSerializer.Serialize(mergedDictionary["product_usage"]),
                    ProductTextVector = textVectors[textVectors.Count - 1]
                };

                List<long> ids = new();
                for (int i = 0; i < existingPreferenceResponse?.FieldsData.Count; i++)
                {
                    if (existingPreferenceResponse?.FieldsData[i].FieldName == "id")
                    {
                        for (int j = 0; j < existingPreferenceResponse.FieldsData[i].Scalars.LongData.Data.Count; j++)
                        {
                            ids.Add(existingPreferenceResponse.FieldsData[i].Scalars.LongData.Data[j]);
                        }
                    }
                }
                _logger.LogInformation(JsonSerializer.Serialize(ids));
                await DeleteUserProductPreference(ids[0]);
                await CreateUserProductPreference(userProductPreference);
            }

            return await Task.FromResult(new ActionStatusReponse
            {
                Status = "Success"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception: {ex.Message}");
            _logger.LogError(ex.StackTrace);
            return await Task.FromResult(new ActionStatusReponse
            {
                Status = "Failed"
            });
        }
    }

    private async Task<string> EncodeText(string searchQuery, EncoderModel model)
    {
        if (model == EncoderModel.CLIP)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("accept", "application/json");

                var payload = new
                {
                    texts = new string[] { searchQuery },
                    images = new string[] { }
                };

                var response = await httpClient.PostAsync($"{_config.GetConnectionString("CLIP")}/vectorize", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                var responseBody = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Product {searchQuery} encoded successfully.");
                    JsonDocument json = JsonDocument.Parse(responseBody);
                    if (json.RootElement.TryGetProperty("textVectors", out var value))
                    {
                        return value.ToString();
                    }
                }
                else
                {
                    _logger.LogError($"Failed to encode product {searchQuery}. Error: " + response.StatusCode);
                    _logger.LogError(responseBody);
                }
            }
        }
        if (model == EncoderModel.BERT)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("accept", "application/json");

                var payload = new
                {
                    text = searchQuery
                };

                var response = await httpClient.PostAsync($"{_config.GetConnectionString("BERT")}/vectors", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                var responseBody = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Product {searchQuery} encoded successfully.");
                    JsonDocument json = JsonDocument.Parse(responseBody);
                    if (json.RootElement.TryGetProperty("vector", out var value))
                    {
                        return value.ToString();
                    }
                }
                else
                {
                    _logger.LogError($"Failed to encode product {searchQuery}. Error: " + response.StatusCode);
                    _logger.LogError(responseBody);
                }
            }
        }
        return string.Empty;
    }

    private async Task<string> EncodeImage(string imageBase64)
    {
        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var payload = new
            {
                texts = new string[] { },
                images = new string[] { imageBase64 }
            };

            var response = await httpClient.PostAsync($"{_config.GetConnectionString("CLIP")}/vectorize", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var responseBody = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"Product image encoded successfully.");
                JsonDocument json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("imageVectors", out var value))
                {
                    return value.ToString();
                }
            }
            else
            {
                _logger.LogError($"Failed to encode product image. Error: " + response.StatusCode);
                _logger.LogError(responseBody);
            }
        }
        return string.Empty;
    }

    private async Task CreateUserProductPreference(UserProductPreference userProductPreference)
    {
        var userProductPreferencePayload = new
        {
            collection_name = "user_product_preference",
            fields_data = new[]
            {
                    new
                    {
                        field_name = "buyer_email",
                        type = 21,
                        field = new object[] { userProductPreference.BuyerEmail }
                    },
                    new
                    {
                        field_name = "product_color",
                        type = 21,
                        field = new object[] { userProductPreference.ProductColor }
                    },
                    new
                    {
                        field_name = "product_season",
                        type = 21,
                        field = new object[] { userProductPreference.ProductSeason }
                    },
                    new
                    {
                        field_name = "product_usage",
                        type = 21,
                        field = new object[] { userProductPreference.ProductUsage }
                    },
                    new
                    {
                        field_name = "product_gender",
                        type = 21,
                        field = new object[] { userProductPreference.ProductGender }
                    },
                    new
                    {
                        field_name = "product_text_vector",
                        type = 101,
                        field = new object[] { userProductPreference.ProductTextVector }
                    }
                },
            num_rows = 1
        };

        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var response = await httpClient.PostAsync($"{_config.GetConnectionString("MilvusUrl")}/api/v1/entities", new StringContent(JsonSerializer.Serialize(userProductPreferencePayload), Encoding.UTF8, "application/json"));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to store user product preference for {userProductPreference.BuyerEmail}.");

            _logger.LogInformation($"User product preference for {userProductPreference.BuyerEmail} stored successfully.");
            _logger.LogInformation(responseBody);
        }
    }

    private async Task DeleteUserProductPreference(long id)
    {
        var userProductPreferencePayload = new
        {
            collection_name = "user_product_preference",
            expr = $"id in [{id}]",
        };

        using (var httpClient = new HttpClient())
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{_config.GetConnectionString("MilvusUrl")}/api/v1/entities");
            request.Content = new StringContent(JsonSerializer.Serialize(userProductPreferencePayload), Encoding.UTF8, "application/json");
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var response = await httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to delete user product preference for {id}.");

            _logger.LogInformation($"User product preference for {id} deleted successfully.");
            _logger.LogInformation(responseBody);
        }
    }

    private async Task SendDataToMilvus(UserProductPreference preference, float[] textVector)
    {
        var userProductPreferencePayload = new
        {
            collection_name = "user_product_preference",
            fields_data = new[]
            {
                    new
                    {
                        field_name = "buyer_email",
                        type = 21,
                        field = new object[] { preference.BuyerEmail }
                    },
                    new
                    {
                        field_name = "product_color",
                        type = 21,
                        field = new object[] { preference.ProductColor }
                    },
                    new
                    {
                        field_name = "product_gender",
                        type = 21,
                        field = new object[] { preference.ProductGender }
                    },
                    new
                    {
                        field_name = "product_season",
                        type = 21,
                        field = new object[] { preference.ProductSeason }
                    },
                    new
                    {
                        field_name = "product_usage",
                        type = 21,
                        field = new object[] { preference.ProductUsage }
                    },
                    new
                    {
                        field_name = "product_text_vector",
                        type = 101,
                        field = new object[] { preference.ProductTextVector }
                    }
                },
            num_rows = 1
        };

        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var response = await httpClient.PostAsync($"{_config.GetConnectionString("MilvusUrl")}/api/v1/entities", new StringContent(JsonSerializer.Serialize(userProductPreferencePayload), Encoding.UTF8, "application/json"));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to store user product preference for {preference.BuyerEmail}.");

            _logger.LogInformation($"User product preference for {preference.BuyerEmail} stored successfully.");
            _logger.LogInformation(responseBody);
        }
    }

    private Dictionary<string, List<Dictionary<string, string>>> BuildDictionary(QueryResults queryResponse)
    {
        Dictionary<string, List<Dictionary<string, string>>> dictionary = new Dictionary<string, List<Dictionary<string, string>>>();

        foreach (var fieldData in queryResponse.FieldsData)
        {
            string fieldName = fieldData.FieldName.ToString();
            if (fieldName == "id")
                continue;

            if (dictionary.ContainsKey(fieldName))
                continue;

            var data = fieldData.Scalars.StringData.Data;

            Dictionary<string, string> fieldValues = new Dictionary<string, string>();

            foreach (string value in data)
            {
                if (fieldValues.ContainsKey(value))
                    fieldValues[value] = (int.Parse(fieldValues[value]) + 1).ToString();
                else
                    fieldValues[value] = "1";
            }

            dictionary[fieldName] = new List<Dictionary<string, string>> { fieldValues };
        }

        return dictionary;
    }

    public Dictionary<string, List<Dictionary<string, string>>> MergeDictionaries(Dictionary<string, string> oldDictionary, Dictionary<string, List<Dictionary<string, string>>> newDictionary)
    {
        var mergedDictionary = new Dictionary<string, string>();
        var returnDict = new Dictionary<string, List<Dictionary<string, string>>>();
        string altKey;

        foreach (var pair in newDictionary)
        {
            if (pair.Key.Contains("color"))
            {
                altKey = "product_color";
            }
            else
            {
                altKey = $"product_{pair.Key}";
            }
            foreach (var pair1 in newDictionary[pair.Key][0])
            {
                mergedDictionary[$"{altKey}:{pair1.Key}"] = pair1.Value;
            }
        }

        foreach (var pair in oldDictionary)
        {
            if (pair.Key.Contains("color"))
            {
                altKey = "product_color";
            }
            else
            {
                altKey = $"{pair.Key}";
            }
            foreach (var property in JsonDocument.Parse(pair.Value).RootElement.EnumerateObject())
            {
                if (!mergedDictionary.ContainsKey($"{altKey}:{property.Name}"))
                {
                    mergedDictionary.Add($"{altKey}:{property.Name}", property.Value.ToString());
                }
            }
        }

        var returneeDict = new Dictionary<string, List<Dictionary<string, string>>>();
        foreach (var pair in mergedDictionary)
        {
            var newTempDict = new Dictionary<string, string>();
            _logger.LogInformation(pair.Key.Split(":")[0]);
            if (returneeDict.ContainsKey(pair.Key.Split(":")[0]))
            {
                newTempDict.Add(pair.Key.Split(":")[1], pair.Value);
                foreach (var k in returneeDict[pair.Key.Split(":")[0]][0])
                {
                    newTempDict.Add(k.Key, k.Value);
                }
                returneeDict[pair.Key.Split(":")[0]][0] = newTempDict;
            }
            else
            {
                newTempDict.Add(pair.Key.Split(":")[1], pair.Value);
                returneeDict.Add(pair.Key.Split(":")[0], new List<Dictionary<string, string>> { newTempDict });
            }
        }

        return returneeDict;
    }

    private async Task<QueryResults> CheckUserProductPreference(string buyerEmail)
    {
        var queryRequest = new QueryRequest
        {
            CollectionName = "user_product_preference",
            OutputFields = { "product_color", "product_season", "product_usage", "product_gender" }, // text_vector?
            Expr = $"buyer_email == \"{buyerEmail}\"",
            PartitionNames = { "_default" },
        };
        return await _milvus.QueryAsync(queryRequest);
    }

    private bool IsBase64String(string base64)
    {
        Span<byte> buffer = new Span<byte>(new byte[base64.Length]);
        return Convert.TryFromBase64String(base64, buffer, out int bytesParsed);
    }
}