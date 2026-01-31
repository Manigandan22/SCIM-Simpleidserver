using SimpleIdServer.Scim.Domains;
using SimpleIdServer.Scim.Exceptions;
using System.Linq;

namespace Scim.Api.Validators
{
    public class UserValidator
    {
        private const string UserNameSchemaId = "urn:ietf:params:scim:schemas:core:2.0:User:userName";

        public void Validate(SCIMRepresentation representation)
        {
            if (representation.ResourceType != "User") return;

            var userNameAttr = representation.FlatAttributes.FirstOrDefault(a => a.SchemaAttributeId == UserNameSchemaId);
            if (userNameAttr == null || string.IsNullOrWhiteSpace(userNameAttr.ValueString))
            {
                throw new SCIMBadSyntaxException("userName is required");
            }

            var emailAttributes = representation.FlatAttributes.Where(a => a.SchemaAttributeId.EndsWith("emails:value"));
            foreach (var emailAttr in emailAttributes)
            {
                if (!string.IsNullOrEmpty(emailAttr.ValueString) && !emailAttr.ValueString.Contains("@"))
                {
                    throw new SCIMBadSyntaxException($"Invalid email format: {emailAttr.ValueString}");
                }
            }
        }
    }
}
