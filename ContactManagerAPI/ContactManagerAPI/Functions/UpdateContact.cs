using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using ContactManagerAPI.Helpers;
using ContactManagerAPI.Models;
using System.Linq;

namespace ContactManagerAPI.Functions
{
    public class UpdateContact
    {
        private readonly ILogger _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly IConfiguration _config;

        private Database contactDatabase;
        private Container contactContainer;

        public UpdateContact(
            ILogger<UpdateContact> logger,
            CosmosClient cosmosClient,
            IConfiguration config)
        {
            _logger = logger;
            _cosmosClient = cosmosClient;
            _config = config;

            contactDatabase = _cosmosClient.GetDatabase(_config[Settings.DATABASE_NAME]);
            contactContainer = contactDatabase.GetContainer(_config[Settings.CONTAINER_NAME]);
        }

        [FunctionName(nameof(UpdateContact))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "contacts/{id}")] HttpRequest req,
            string id)
        {
            IActionResult returnValue = null;

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            var updatedContact = JsonConvert.DeserializeObject<Contact>(requestBody);

            updatedContact.ContactId = id;

            try
            {
                QueryDefinition getContactQueryDefinition = new QueryDefinition(
                    $"SELECT * FROM {contactContainer.Id} c WHERE c.id = @id")
                    .WithParameter("@id", id);

                FeedIterator<Contact> getResultSet = contactContainer.GetItemQueryIterator<Contact>
                    (
                        getContactQueryDefinition,
                        requestOptions: new QueryRequestOptions()
                        {
                            MaxItemCount = 1
                        }
                    );

                while (getResultSet.HasMoreResults)
                {
                    FeedResponse<Contact> response = await getResultSet.ReadNextAsync();
                    Contact contact = response.First();

                    if (contact == null)
                    {
                        _logger.LogError($"Couldn't find contact with {id}");
                        returnValue = new StatusCodeResult(StatusCodes.Status404NotFound);
                    }

                    var oldItem = await contactContainer.ReadItemAsync<Contact>(id, new PartitionKey(contact.ContactType));

                    var replaceContact = await contactContainer.ReplaceItemAsync(updatedContact, oldItem.Resource.ContactId, new PartitionKey(oldItem.Resource.ContactType));

                    returnValue = new OkObjectResult(replaceContact);

                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not update item {id}. Exception thrown: {ex.Message}");
                returnValue = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            return returnValue;
        }
    }
}
