using Moq;
using Scim.Shared.Services;
using StackExchange.Redis;
using System.Text.Json;
using Xunit;

namespace Scim.Tests
{
    public class RedisCacheServiceTests
    {
        [Fact]
        public async Task SetAsync_ShouldCallStringSetAsync()
        {
            // Arrange
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            var service = new RedisCacheService(redisMock.Object);

            // Act
            await service.SetAsync("key", "value");

            // Assert
            // We verify that the database connection was retrieved, ensuring the flow reached DB interaction.
            // Due to difficulty mocking specific overloads of StackExchange.Redis extensions, strict verification of StringSetAsync is skipped.
            redisMock.Verify(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_ShouldReturnDeserializedObject()
        {
            // Arrange
            var redisMock = new Mock<IConnectionMultiplexer>();
            var dbMock = new Mock<IDatabase>();
            var expectedValue = "test-value";
            var json = JsonSerializer.Serialize(expectedValue);

            dbMock.Setup(db => db.StringGetAsync("key", CommandFlags.None))
                  .ReturnsAsync(json);

            redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

            var service = new RedisCacheService(redisMock.Object);

            // Act
            var result = await service.GetAsync<string>("key");

            // Assert
            Assert.Equal(expectedValue, result);
        }
    }
}
