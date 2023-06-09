# Realsearch

Realsearch is a proof-of-concept search tool that utilizes Milvus as the vector database to enable personalized text and image search, leveraging state-of-the-art machine learning models like CLIP and SBERT, and employing cosine similarity for tailored recommendations based on user preferences and purchase history.

## Description

Realsearch is a proof-of-concept search tool that offers personalized text and image search capabilities based on user preferences. It harnesses an extensive fashion e-commerce dataset to power its search functionality. Leveraging Milvus as the underlying vector database, Realsearch effectively stores and indexes vectors encoded by state-of-the-art machine learning models like CLIP (OpenAI) and SBERT, facilitated by the Sentence-Transformers library. At the core of Realsearch's personalized search capabilities is the utilization of cosine similarity. This technique allows the tool to rearrange search results based on the user's purchase history, ensuring that relevant and tailored recommendations are provided. By incorporating the user's preferences and historical interactions, Realsearch enhances the search experience by delivering more meaningful and accurate results.

## Features

- Users:
  - Versatile Search Modes: Realsearch offers users various search modes to find products. In the default mode (Mode 0), semantic search enables users to search based on product descriptions. In Mode 1, users can describe the physical appearance or use reverse image search for their searches.
  - Personalized Search: Users can build their preference data by making purchases, allowing Realsearch to personalize future searches according to their interests and preferences.

## Running (using existing myntra dataset)

1. Clone the repository.
   ```
   git clone https://github.com/quachhengtony/climan.net.git
   ```
2. Run the docker-compose.local.yml file.
   ```
   docker compose -f docker-compose.local.yml up -d
   ```
3. Create the appropriate Milvus indexes and load the collections into memory.
   Build the indexes:
   ```
   curl -X 'POST' \
       'http://localhost:9091/api/v1/index' \
       -H 'accept: application/json' \
       -H 'Content-Type: application/json' \
       -d '{
       	"collection_name": "product",
       	"field_name": "text_vector",
       	"extra_params": [
       		{
       			"key": "metric_type",
       			"value": "IP"
       		},
       		{
       			"key": "index_type",
       			"value": "IVF_FLAT"
       		},
       		{
       			"key": "params",
       			"value": "{\"nlist\":2048}"
       		}
       	]
       }'
   ```
   ```
   curl -X 'POST' \
       'http://localhost:9091/api/v1/index' \
       -H 'accept: application/json' \
       -H 'Content-Type: application/json' \
       -d '{
       	"collection_name": "product_image",
       	"field_name": "image_vector",
       	"extra_params": [
       		{
       			"key": "metric_type",
       			"value": "IP"
       		},
       		{
       			"key": "index_type",
       			"value": "IVF_FLAT"
       		},
       		{
       			"key": "params",
       			"value": "{\"nlist\":512}"
       		}
       	]
       }'
   ```
   ```
   curl -X 'POST' \
       'http://localhost:9091/api/v1/index' \
       -H 'accept: application/json' \
       -H 'Content-Type: application/json' \
       -d '{
       	"collection_name": "user_product_preference",
       	"index_name": "buyer_email_index",
       	"field_name": "buyer_email"
       }'
   ```
   Load the collections into memory:
   ```
   curl -X 'POST' \
       'http://localhost:9091/api/v1/collection/load' \
       -H 'accept: application/json' \
       -H 'Content-Type: application/json' \
       -d '{
           "collection_name": "product"
       }'
   ```
   ```
   curl -X 'POST' \
       'http://localhost:9091/api/v1/collection/load' \
       -H 'accept: application/json' \
       -H 'Content-Type: application/json' \
       -d '{
           "collection_name": "product_image"
       }'
   ```
   ```
   curl -X 'POST' \
       'http://localhost:9091/api/v1/collection/load' \
       -H 'accept: application/json' \
       -H 'Content-Type: application/json' \
       -d '{
           "collection_name": "user_product_preference"
       }'
   ```
4. Perform searches with the API using server reflection (localhost:5154) or the product.proto file in API.Contracts/Protos to build requests.

## Running (using a new dataset)

1. Clone the repository.
2. Comment out the api service and run the docker-compose.local.yml file.
3. Configure the input/output folder paths and run the image preprocessor.
4. Configure the urls, input file paths, Milvus metadata and run the ingestor.
5. Uncomment the api service and run the docker-compose.local.yml file again.
6. Create the appropriate Milvus indexes and load the collections into memory (see above).
7. Perform searches with the API by using server reflection (localhost:5154) or the product.proto file in API.Contracts/Protos to build requests.

## Technologies

- Frameworks: [.NET](https://dotnet.microsoft.com/en-us/).
- Database: [Milvus](https://milvus.io/).
- Others: [Docker](https://www.docker.com/).

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change. Please make sure to update tests as appropriate.

## License

[MIT](https://choosealicense.com/licenses/mit/)
