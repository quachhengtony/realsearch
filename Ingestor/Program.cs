namespace Realsearch.Ingestor;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

using API.Contracts;
using Realsearch.Ingestor.Models;
using System.Linq;

class Program
{
    private const string InputFolderPath = @"myntradataset\preprocessed-images";
    private const string InputCsvFilePath = @"myntradataset\styles.csv";
    private const string BertUrl = @"http://localhost:9089";
    private const string ClipUrl = @"http://localhost:9090";
    private const string MilvusUrl = @"http://localhost:9091";
    private const string CollectionName = @"product";
    private const string CollectionDesciption = @"E-commerce products";

    static async Task Main(string[] args)
    {
        await UpsertMilvusCollection();

        var styles = ReadCsvFile(InputCsvFilePath);
        string[] imagePaths = Directory.GetFiles(InputFolderPath, "*.png");

        int loopCount = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = 6 };
        await Parallel.ForEachAsync(imagePaths, options, async (imagePath, _) =>
        {
            loopCount++;
            Console.WriteLine($"Ingesting product number {loopCount}");

            try
            {
                string imageName = Path.GetFileNameWithoutExtension(imagePath);
                CsvProduct? csvProduct = styles.GetValueOrDefault(imageName);

                if (csvProduct is not null)
                {
                    csvProduct.ImageUrl = @$"file:///D:/Works/Viettel Digital Talent 2023/Stage 1 Project/Realsearch/myntradataset/images/{imageName}.jpg";
                    byte[] imageData = File.ReadAllBytes(imagePath);
                    string base64Image = ConvertToBase64(imageData);

                    string imageVector;
                    string textVector;
                    float[] imageFloats;
                    float[] textFloats;
                    if (!string.IsNullOrEmpty(csvProduct.DisplayName))
                    {
                        imageVector = await EncodeImageTextPair($"a photo of a {csvProduct.DisplayName}" ?? "a photo", base64Image);
                        textVector = await EncodeText(csvProduct.DisplayName);

                        imageVector = imageVector.Substring(2, imageVector.Length - 4);
                        imageFloats = imageVector.Split(",").Select(float.Parse).ToArray();
                        textVector = textVector.Substring(1, textVector.Length - 2);
                        textFloats = textVector.Split(",").Select(float.Parse).ToArray();

                        await SendDataToMilvus(csvProduct, imageFloats, textFloats);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to ingest data {imagePath}. Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        });
    }

    static Dictionary<string, CsvProduct> ReadCsvFile(string csvFilePath)
    {
        CsvProduct product;
        var csvDictionary = new Dictionary<string, CsvProduct>();

        using (var streamReader = new StreamReader(csvFilePath))
        using (var csvReader = new CsvReader(streamReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null
        }))
        {
            csvReader.Read();
            csvReader.ReadHeader();

            while (csvReader.Read())
            {
                var id = csvReader.GetField<string>("id");
                var gender = csvReader.GetField<string>("gender");
                var masterCategory = csvReader.GetField<string>("masterCategory");
                var subCategory = csvReader.GetField<string>("subCategory");
                var articleType = csvReader.GetField<string>("articleType");
                var baseColor = csvReader.GetField<string>("baseColour");
                var season = csvReader.GetField<string>("season");
                var year = csvReader.GetField<string>("year");
                var usage = csvReader.GetField<string>("usage");
                var displayName = csvReader.GetField<string>("productDisplayName");

                product = new CsvProduct
                {
                    Gender = gender,
                    MasterCategory = masterCategory,
                    SubCategory = subCategory,
                    ArticleType = articleType,
                    BaseColor = baseColor,
                    Season = season,
                    Year = year,
                    Usage = usage,
                    DisplayName = displayName
                };

                if (id is not null)
                    csvDictionary.Add(id, product);
            }
        }

        return csvDictionary;
    }

    static string ConvertToBase64(byte[] imageData) => Convert.ToBase64String(imageData);

    static async Task UpsertMilvusCollection()
    {
        var productImageSchema = new CreateCollectionRequest
        {
            collection_name = $"{CollectionName}_image",
            schema = new CollectionSchema
            {
                autoID = false,
                description = $"{CollectionName}'s image collection",
                fields = new List<CollectionField>
                        {
                            new CollectionField
                            {
                                name = "id",
                                description = "Image id",
                                is_primary_key = true,
                                autoID = true,
                                data_type = 5
                            },
                            new CollectionField
                            {
                                name = "product_id",
                                description = "Image's product id",
                                is_primary_key = false,
                                data_type = 5
                            },
                            new CollectionField
                            {
                                name = "image_vector",
                                description = "Image vector",
                                is_primary_key = false,
                                data_type = 101,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "dim", value = "512" }
                                }
                            }
                        },
                name = $"{CollectionName}_image"
            }
        };

        var productSchema = new CreateCollectionRequest
        {
            collection_name = CollectionName,
            schema = new CollectionSchema
            {
                autoID = false,
                description = CollectionDesciption,
                fields = new List<CollectionField>
                        {
                            new CollectionField
                            {
                                name = "id",
                                description = "Product id",
                                is_primary_key = true,
                                autoID = false,
                                data_type = 5
                            },
                            new CollectionField
                            {
                                name = "gender",
                                description = "Product gender",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "master_category",
                                description = "Product master category",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "subcategory",
                                description = "Product subcategory",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "article_type",
                                description = "Product article type",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "base_color",
                                description = "Product base color",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                              new CollectionField
                            {
                                name = "season",
                                description = "Product season",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "year",
                                description = "Product year",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "usage",
                                description = "Product usage",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "25" }
                                }
                            },
                            new CollectionField
                            {
                                name = "display_name",
                                description = "Product display name",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "50" }
                                }
                            },
                            new CollectionField
                            {
                                name = "image_url",
                                description = "Product image url",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "2050" }
                                }
                            },
                            new CollectionField
                            {
                                name = "text_vector",
                                description = "Product text vector",
                                is_primary_key = false,
                                data_type = 101,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "dim", value = "384" }
                                }
                            },
                        },
                name = CollectionName
            }
        };

        var userProductPreferenceSchema = new CreateCollectionRequest
        {
            collection_name = "user_product_preference",
            schema = new CollectionSchema
            {
                autoID = false,
                description = "User product preference collection",
                fields = new List<CollectionField>
                        {
                            new CollectionField
                            {
                                name = "id",
                                description = "User product preference id",
                                is_primary_key = true,
                                autoID = true,
                                data_type = 5
                            },
                            new CollectionField
                            {
                                name = "buyer_email",
                                description = "Buyer's email",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "350" }
                                }
                            },
                            new CollectionField
                            {
                                name = "product_color",
                                description = "Preferred product color",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "250" }
                                }
                            },
                            new CollectionField
                            {
                                name = "product_season",
                                description = "Preferred product season",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "250" }
                                }
                            },
                            new CollectionField
                            {
                                name = "product_usage",
                                description = "Preferred product usage",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "250" }
                                }
                            },
                            new CollectionField
                            {
                                name = "product_gender",
                                description = "Preferred product gender",
                                is_primary_key = false,
                                data_type = 21,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "max_length", value = "250" }
                                }
                            },
                            new CollectionField
                            {
                                name = "product_text_vector",
                                description = "Preferred product text vector",
                                is_primary_key = false,
                                data_type = 101,
                                type_params = new List<TypeParams>
                                {
                                    new TypeParams { key = "dim", value = "384" }
                                }
                            },
                        },
                name = "user_product_preference"
            }
        };

        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var checkCollectionRequest = new HttpRequestMessage(HttpMethod.Get, $"{MilvusUrl}/api/v1/collection/existence")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { collection_name = CollectionName }))
            };
            var response = await httpClient.SendAsync(checkCollectionRequest);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to check collection {CollectionName} existence.");

            string responseBody = await response.Content.ReadAsStringAsync();
            JsonDocument json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("value", out _))
            {
                Console.WriteLine($"Collection {CollectionName} already exists. Skipping the creation of collection {CollectionName}, collection {CollectionName}_image, and collection user_product_preference.");
                return;
            }

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var productSchemaJson = JsonSerializer.Serialize(productSchema, jsonSerializerOptions);
            var productImageSchemaJson = JsonSerializer.Serialize(productImageSchema, jsonSerializerOptions);
            var userProductPreferenceSchemaJson = JsonSerializer.Serialize(userProductPreferenceSchema, jsonSerializerOptions);

            response = await httpClient.PostAsync($"{MilvusUrl}/api/v1/collection", new StringContent(productSchemaJson, Encoding.UTF8, "application/json"));
            responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode || responseBody != "{}")
                throw new HttpRequestException($"Failed to create collection {CollectionName}.");

            Console.WriteLine($"Collection {CollectionName} created successfully.");
            response = await httpClient.PostAsync($"{MilvusUrl}/api/v1/collection", new StringContent(productImageSchemaJson, Encoding.UTF8, "application/json"));
            responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode || responseBody != "{}")
                throw new HttpRequestException($"Failed to create collection {CollectionName}_image.");

            Console.WriteLine($"Collection {CollectionName}_image created successfully.");
            response = await httpClient.PostAsync($"{MilvusUrl}/api/v1/collection", new StringContent(userProductPreferenceSchemaJson, Encoding.UTF8, "application/json"));
            responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode || responseBody != "{}")
                throw new HttpRequestException($"Failed to create collection user_product_preference.");

            Console.WriteLine($"Collection user_product_preference created successfully.");
        }
    }

    static async Task<string> EncodeImageTextPair(string text, string base64Image)
    {
        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var payload = new
            {
                texts = new string[] { text },
                images = new string[] { base64Image }
            };

            var response = await httpClient.PostAsync($"{ClipUrl}/vectorize", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Image-text pair {text} encoded successfully.");
                JsonDocument json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("imageVectors", out var value))
                {
                    return value.ToString();
                }
            }
            else
            {
                Console.WriteLine($"Failed to encode image-text pair {text}. Error: " + response.StatusCode);
                Console.WriteLine(responseBody);
            }
        }
        return string.Empty;
    }

    static async Task<string> EncodeText(string text)
    {
        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var payload = new
            {
                text = text
            };

            var response = await httpClient.PostAsync($"{BertUrl}/vectors", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Text {text} encoded successfully.");
                JsonDocument json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("vector", out var value))
                {
                    return value.ToString();
                }
            }
            else
            {
                Console.WriteLine($"Failed to encode text {text}. Error: " + response.StatusCode);
                Console.WriteLine(responseBody);
            }
        }
        return string.Empty;
    }

    static async Task SendDataToMilvus(CsvProduct csvProduct, float[] imageVector, float[] textVector)
    {
        var productId = new Random().Next();
        var productPayload = new
        {
            collection_name = CollectionName,
            fields_data = new[]
            {
                    new
                    {
                        field_name = "id",
                        type = 5,
                        field = new object[] { productId }
                    },
                    new
                    {
                        field_name = "gender",
                        type = 21,
                        field = new object[] { csvProduct.Gender }
                    },
                    new
                    {
                        field_name = "master_category",
                        type = 21,
                        field = new object[] { csvProduct.MasterCategory }
                    },
                    new
                    {
                        field_name = "subcategory",
                        type = 21,
                        field = new object[] { csvProduct.SubCategory }
                    },
                    new
                    {
                        field_name = "article_type",
                        type = 21,
                        field = new object[] { csvProduct.ArticleType }
                    },
                    new
                    {
                        field_name = "base_color",
                        type = 21,
                        field = new object[] { csvProduct.BaseColor }
                    },
                    new
                    {
                        field_name = "season",
                        type = 21,
                        field = new object[] { csvProduct.Season }
                    },
                    new
                    {
                        field_name = "year",
                        type = 21,
                        field = new object[] { csvProduct.Year }
                    },
                    new
                    {
                        field_name = "usage",
                        type = 21,
                        field = new object[] { csvProduct.Usage }
                    },
                    new
                    {
                        field_name = "display_name",
                        type = 21,
                        field = new object[] { csvProduct.DisplayName }
                    },
                    new
                    {
                        field_name = "image_url",
                        type = 21,
                        field = new object[] { csvProduct.ImageUrl }
                    },
                    new
                    {
                        field_name = "text_vector",
                        type = 101,
                        field = new object[] { textVector }
                    },
                },
            num_rows = 1
        };

        var imagePayload = new
        {
            collection_name = $"{CollectionName}_image",
            fields_data = new[]
            {
                    new
                    {
                        field_name = "product_id",
                        type = 5,
                        field = new object[] { productId }
                    },
                    new
                    {
                        field_name = "image_vector",
                        type = 101,
                        field = new object[] { imageVector }
                    }
                },
            num_rows = 1
        };

        using (var httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Add("accept", "application/json");

            var response = await httpClient.PostAsync($"{MilvusUrl}/api/v1/entities", new StringContent(JsonSerializer.Serialize(productPayload), Encoding.UTF8, "application/json"));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to store product {csvProduct.DisplayName}.");

            Console.WriteLine($"Product {csvProduct.DisplayName} with id {productId} stored successfully.");
            Console.WriteLine(responseBody);
            response = await httpClient.PostAsync($"{MilvusUrl}/api/v1/entities", new StringContent(JsonSerializer.Serialize(imagePayload), Encoding.UTF8, "application/json"));
            responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to store image of product {csvProduct.DisplayName}.");

            Console.WriteLine($"Image of product {csvProduct.DisplayName} with id {productId} stored successfully.");
            Console.WriteLine(responseBody);
        }
    }
}
