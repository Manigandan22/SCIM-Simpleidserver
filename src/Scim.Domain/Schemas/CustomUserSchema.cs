using SimpleIdServer.Scim.Domains;
using SimpleIdServer.Scim.Domains.Builders;
using System.Collections.Generic;

namespace Scim.Domain.Schemas
{
    public static class CustomUserSchema
    {
        public const string ResourceType = "User";
        public const string SchemaId = "urn:example:params:scim:schemas:extension:custom:2.0:User";
        public const string Name = "CustomUser";
        public const string Description = "Custom User Attributes";

        public static SCIMSchema GetSchema()
        {
            var schemaBuilder = SCIMSchemaBuilder.Create(SchemaId, Name, Description, Description, true);

            // employeeNumber (string, required)
            schemaBuilder.AddStringAttribute("employeeNumber", description: "Employee Number", required: true);

            // departmentCode (string, required)
            schemaBuilder.AddStringAttribute("departmentCode", description: "Department Code", required: true);

            // costCenter (string, optional)
            schemaBuilder.AddStringAttribute("costCenter", description: "Cost Center", required: false);

            // region (string, enum: IN, US, EU)
            schemaBuilder.AddStringAttribute("region", description: "Region", required: false, canonicalValues: new List<string> { "IN", "US", "EU" });

            // isContractor (boolean, default false)
            schemaBuilder.AddBooleanAttribute("isContractor", description: "Is Contractor", required: false);

            return schemaBuilder.Build();
        }
    }
}
