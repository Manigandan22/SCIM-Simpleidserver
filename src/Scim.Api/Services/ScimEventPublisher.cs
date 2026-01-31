using SimpleIdServer.Scim.Persistence;
using SimpleIdServer.Scim.Domains;
using SimpleIdServer.Scim.Parser.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Scim.Shared.Services;
using Scim.Shared.Models;
using Scim.Api.Validators;
using System.Linq;

namespace Scim.Api.Services
{
    public class ScimEventPublisher : ISCIMRepresentationCommandRepository
    {
        private readonly ISCIMRepresentationCommandRepository _inner;
        private readonly IServiceBusPublisher _publisher;
        private readonly UserValidator _userValidator;

        public ScimEventPublisher(ISCIMRepresentationCommandRepository inner, IServiceBusPublisher publisher, UserValidator userValidator)
        {
            _inner = inner;
            _publisher = publisher;
            _userValidator = userValidator;
        }

        public async Task<bool> Add(SCIMRepresentation representation, CancellationToken token)
        {
            _userValidator.Validate(representation);
            var result = await _inner.Add(representation, token);
            if (result)
            {
                await _publisher.PublishAsync(new ScimNotification
                {
                    ResourceType = representation.ResourceType,
                    Action = "Create",
                    ResourceId = representation.Id,
                    Payload = representation.Id
                });
            }
            return result;
        }

        public async Task<bool> Update(SCIMRepresentation representation, CancellationToken token)
        {
            _userValidator.Validate(representation);
            var result = await _inner.Update(representation, token);
            if (result)
            {
                await _publisher.PublishAsync(new ScimNotification
                {
                    ResourceType = representation.ResourceType,
                    Action = "Update",
                    ResourceId = representation.Id
                });
            }
            return result;
        }

        public async Task<bool> Delete(SCIMRepresentation representation, CancellationToken token)
        {
            var result = await _inner.Delete(representation, token);
            if (result)
            {
                await _publisher.PublishAsync(new ScimNotification
                {
                    ResourceType = representation.ResourceType,
                    Action = "Delete",
                    ResourceId = representation.Id
                });
            }
            return result;
        }

        // Bulk operations
        public async Task BulkInsert(IEnumerable<SCIMRepresentationAttribute> attributes, string representationId, bool verify = true)
        {
             await _inner.BulkInsert(attributes, representationId, verify);
        }

        public async Task BulkDelete(IEnumerable<SCIMRepresentationAttribute> attributes, string representationId, bool verify = true)
        {
            await _inner.BulkDelete(attributes, representationId, verify);
        }

        public async Task BulkUpdate(IEnumerable<SCIMRepresentationAttribute> attributes, bool verify = true)
        {
            await _inner.BulkUpdate(attributes, verify);
        }

        // Delegating other members
        public Task<SCIMRepresentation> Get(string id, string resourceType, CancellationToken token) => _inner.Get(id, resourceType, token);
        public Task<ITransaction> StartTransaction(CancellationToken token) => _inner.StartTransaction(token);

        // This method seems to cause issues. It appears ISCIMRepresentationCommandRepository inherits from ISCIMRepresentationQueryRepository?
        // If so, FindRepresentations(SearchSCIMRepresentationsParameter...) belongs to QueryRepository.
        // But the class must implement CommandRepository.
        // The error suggests that _inner.FindRepresentations(...) is resolving to the WRONG overload, trying to match (List<string>, string) signature but with SearchParameter.

        // I will NOT implement the QueryRepository methods here if they are not in CommandRepository interface.
        // But the previous compilation errors said "does not implement interface member ... FindRepresentations".

        // Let's implement ALL methods required by ISCIMRepresentationCommandRepository manually, and check if it builds.

        // Method 1: (List<string>, string, CancellationToken) -> List<SCIMRepresentation>
        public Task<List<SCIMRepresentation>> FindRepresentations(List<string> representationIds, string resourceType, CancellationToken token)
             => _inner.FindRepresentations(representationIds, resourceType, token);

        public Task<List<SCIMRepresentationAttribute>> FindGraphAttributes(string representationId, string value, string schemaAttributeId, CancellationToken token)
             => _inner.FindGraphAttributes(representationId, value, schemaAttributeId, token);

        public Task<List<SCIMRepresentationAttribute>> FindGraphAttributes(IEnumerable<string> representationIds, List<string> values, string schemaAttributeId, string resourceType, CancellationToken token)
             => _inner.FindGraphAttributes(representationIds, values, schemaAttributeId, resourceType, token);

        public Task<List<SCIMRepresentationAttribute>> FindGraphAttributesBySchemaAttributeId(string representationId, string schemaAttributeId, CancellationToken token)
             => _inner.FindGraphAttributesBySchemaAttributeId(representationId, schemaAttributeId, token);

        public Task<List<SCIMRepresentationAttribute>> FindGraphAttributesBySchemaAttributeId(List<string> representationIds, string schemaAttributeId, CancellationToken token)
             => _inner.FindGraphAttributesBySchemaAttributeId(representationIds, schemaAttributeId, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributes(string representationId, SCIMAttributeExpression expression, CancellationToken token)
             => _inner.FindAttributes(representationId, expression, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributes(string representationId, CancellationToken token)
             => _inner.FindAttributes(representationId, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByAproximativeFullPath(string representationId, string fullPath, CancellationToken token)
             => _inner.FindAttributesByAproximativeFullPath(representationId, fullPath, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByExactFullPathAndValues(string representationId, IEnumerable<string> values, CancellationToken token)
             => _inner.FindAttributesByExactFullPathAndValues(representationId, values, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByExactFullPathAndRepresentationIds(string fullPath, IEnumerable<string> representationIds, CancellationToken token)
             => _inner.FindAttributesByExactFullPathAndRepresentationIds(fullPath, representationIds, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesBySchemaAttributeAndValues(string schemaAttributeId, IEnumerable<string> values, CancellationToken token)
             => _inner.FindAttributesBySchemaAttributeAndValues(schemaAttributeId, values, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByComputedValueIndexAndRepresentationId(List<string> representationIds, string computedValueIndex, CancellationToken token)
             => _inner.FindAttributesByComputedValueIndexAndRepresentationId(representationIds, computedValueIndex, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByReference(List<string> representationIds, string schemaAttributeId, string value, CancellationToken token)
             => _inner.FindAttributesByReference(representationIds, schemaAttributeId, value, token);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByValue(string schemaAttributeId, string value)
             => _inner.FindAttributesByValue(schemaAttributeId, value);

        public Task<List<SCIMRepresentationAttribute>> FindAttributesByValue(string schemaAttributeId, int value)
             => _inner.FindAttributesByValue(schemaAttributeId, value);
    }
}
