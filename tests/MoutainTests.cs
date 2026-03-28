using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using src.Controllers;
using src.Models;

namespace tests
{
    public class MountainControllerTests : IDisposable
    {
        private readonly GoreDBContext _context;
        private readonly Mock<ILogger<UserController>> _loggerMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly MountainController _controller;

        public MountainControllerTests()
        {
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _loggerMock = new Mock<ILogger<UserController>>();
            _configMock = new Mock<IConfiguration>();

            _controller = new MountainController(_loggerMock.Object, _configMock.Object, _context);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        // Helper method to create test mountains
        private Mountain CreateTestMountain(
            Guid? id = null,
            string name = "Test Mountain",
            int height = 1000,
            int regionId = 1,
            decimal lat = 45.0m,
            decimal lon = 10.0m)
        {
            return new Mountain
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                Height = height,
                RegionId = regionId,
                Lat = lat,
                Lon = lon
            };
        }

        // ---------- GetMountains Tests ----------

        [Fact]
        public void GetMountains_WhenMountainsExist_ReturnsOkWithMountainList()
        {
            // Arrange
            var mountain1 = CreateTestMountain(name: "Mountain 1", height: 1500);
            var mountain2 = CreateTestMountain(name: "Mountain 2", height: 2000, regionId: 2);
            var mountain3 = CreateTestMountain(name: "Mountain 3", height: 1200);

            _context.Mountains.AddRange(mountain1, mountain2, mountain3);
            _context.SaveChanges();

            // Act
            var result = _controller.GetMountains();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var mountains = Assert.IsType<List<MountainDto>>(okResult.Value);
            Assert.Equal(3, mountains.Count);

            // Verify first mountain
            Assert.Equal(mountain1.Id, mountains[0].Id);
            Assert.Equal(mountain1.Name, mountains[0].Name);
            Assert.Equal(mountain1.Height, mountains[0].Height);
            Assert.Equal(mountain1.RegionId, mountains[0].RegionId);
            Assert.Equal(mountain1.Lat, mountains[0].Lat);
            Assert.Equal(mountain1.Lon, mountains[0].Lon);

            // Verify second mountain
            Assert.Equal(mountain2.Id, mountains[1].Id);
            Assert.Equal(mountain2.Name, mountains[1].Name);

            // Verify third mountain
            Assert.Equal(mountain3.Id, mountains[2].Id);
            Assert.Equal(mountain3.Name, mountains[2].Name);
        }

        [Fact]
        public void GetMountains_WhenNoMountainsExist_ReturnsNotFound()
        {
            // Arrange
            // No mountains added to context

            // Act
            var result = _controller.GetMountains();

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }
    }
}