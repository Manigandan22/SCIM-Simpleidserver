using SimpleIdServer.Scim.Domains;
using SimpleIdServer.Scim.Exceptions;
using Scim.Api.Validators;
using Xunit;
using System.Collections.Generic;

namespace Scim.Tests
{
    public class ValidationTests
    {
        private readonly UserValidator _validator;

        public ValidationTests()
        {
            _validator = new UserValidator();
        }

        [Fact]
        public void Validate_ShouldThrow_WhenUserNameIsMissing()
        {
            // Arrange
            var representation = new SCIMRepresentation
            {
                ResourceType = "User",
                FlatAttributes = new List<SCIMRepresentationAttribute>()
            };

            // Act & Assert
            Assert.Throws<SCIMBadSyntaxException>(() => _validator.Validate(representation));
        }

        [Fact]
        public void Validate_ShouldPass_WhenUserNameIsPresent()
        {
            // Arrange
            var representation = new SCIMRepresentation
            {
                ResourceType = "User",
                FlatAttributes = new List<SCIMRepresentationAttribute>
                {
                    new SCIMRepresentationAttribute(System.Guid.NewGuid().ToString(), System.Guid.NewGuid().ToString())
                    {
                        SchemaAttributeId = "urn:ietf:params:scim:schemas:core:2.0:User:userName",
                        ValueString = "jdoe"
                    }
                }
            };

            // Act
            _validator.Validate(representation);
        }

        [Fact]
        public void Validate_ShouldThrow_WhenEmailIsInvalid()
        {
            // Arrange
            var representation = new SCIMRepresentation
            {
                ResourceType = "User",
                FlatAttributes = new List<SCIMRepresentationAttribute>
                {
                    new SCIMRepresentationAttribute(System.Guid.NewGuid().ToString(), System.Guid.NewGuid().ToString())
                    {
                        SchemaAttributeId = "urn:ietf:params:scim:schemas:core:2.0:User:userName",
                        ValueString = "jdoe"
                    },
                    new SCIMRepresentationAttribute(System.Guid.NewGuid().ToString(), System.Guid.NewGuid().ToString())
                    {
                        SchemaAttributeId = "urn:ietf:params:scim:schemas:core:2.0:User:emails:value",
                        ValueString = "invalid-email"
                    }
                }
            };

            // Act & Assert
            Assert.Throws<SCIMBadSyntaxException>(() => _validator.Validate(representation));
        }
    }
}
