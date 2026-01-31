using SimpleIdServer.Scim.Domains;
using SimpleIdServer.Scim.Domains.Builders;
using System.Collections.Generic;

namespace Scim.Api.Schemas
{
    public static class CustomSchemas
    {
        public static SCIMSchema GetUserSchema()
        {
            var schemaBuilder = SCIMSchemaBuilder.Create("urn:custom:params:scim:schemas:extension:CustomUser", "CustomUser", "Custom User Attributes", "Custom User Attributes", true);
            schemaBuilder.AddStringAttribute("Department", callback: c =>
            {
                c.SetCaseExact(false)
                 .SetMultiValued(false)
                 .SetRequired(false)
                 .SetMutability(SCIMSchemaAttributeMutabilities.READWRITE)
                 .SetReturned(SCIMSchemaAttributeReturned.DEFAULT);
            });
            return schemaBuilder.Build();
        }
    }
}
