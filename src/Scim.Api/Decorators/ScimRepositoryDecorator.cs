using SimpleIdServer.Scim.Persistence;
using SimpleIdServer.Scim.Domains;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Scim.Domain.Services;
using Scim.Contracts;
using Scim.Api.Validators;
using System.Linq;
using SimpleIdServer.Scim.Parser.Expressions;

namespace Scim.Api.Decorators
{
    public class ScimRepositoryDecorator : ISCIMRepresentationCommandRepository
    {
        private readonly ISCIMRepresentationCommandRepository _inner;
        private readonly IScimNotificationService _notificationService;
        private readonly UserValidator _userValidator;

        public ScimRepositoryDecorator(ISCIMRepresentationCommandRepository inner, IScimNotificationService notificationService, UserValidator userValidator)
        {
            _inner = inner;
            _notificationService = notificationService;
            _userValidator = userValidator;
        }

        public async Task<bool> Add(SCIMRepresentation representation, CancellationToken token)
        {
            _userValidator.Validate(representation);
            var result = await _inner.Add(representation, token);
            if (result)
            {
                var evt = new RepresentationAddedEvent
                {
                    Id = representation.Id,
                    ResourceType = representation.ResourceType,
                    RepresentationJson = representation.Id, // TODO: Serialize full if needed
                    Version = representation.Version
                };
                await _notificationService.NotifyAddedAsync(evt, token);
            }
            return result;
        }

        public async Task<bool> Update(SCIMRepresentation representation, CancellationToken token)
        {
            _userValidator.Validate(representation);
            var result = await _inner.Update(representation, token);
            if (result)
            {
                var evt = new RepresentationUpdatedEvent
                {
                    Id = representation.Id,
                    ResourceType = representation.ResourceType,
                    RepresentationJson = representation.Id,
                    Version = representation.Version
                };
                await _notificationService.NotifyUpdatedAsync(evt, token);
            }
            return result;
        }

        public async Task<bool> Delete(SCIMRepresentation representation, CancellationToken token)
        {
            var result = await _inner.Delete(representation, token);
            if (result)
            {
                var evt = new RepresentationRemovedEvent
                {
                    Id = representation.Id,
                    ResourceType = representation.ResourceType
                };
                await _notificationService.NotifyRemovedAsync(evt, token);
            }
            return result;
        }

        // Delegating methods
        public Task<SCIMRepresentation> Get(string id, string resourceType, CancellationToken token) => _inner.Get(id, resourceType, token);
        public Task<ITransaction> StartTransaction(CancellationToken token) => _inner.StartTransaction(token);

        public Task BulkInsert(IEnumerable<SCIMRepresentationAttribute> attributes, string representationId, bool verify = true) => _inner.BulkInsert(attributes, representationId, verify);
        public Task BulkDelete(IEnumerable<SCIMRepresentationAttribute> attributes, string representationId, bool verify = true) => _inner.BulkDelete(attributes, representationId, verify);
        public Task BulkUpdate(IEnumerable<SCIMRepresentationAttribute> attributes, bool verify = true) => _inner.BulkUpdate(attributes, verify);

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
