using AzureAISearchSimulator.Api.Controllers;
using AzureAISearchSimulator.Api.Services.Authentication;
using AzureAISearchSimulator.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace AzureAISearchSimulator.Api.Tests.Controllers;

/// <summary>
/// Unit tests for TokenController.
/// </summary>
public class TokenControllerTests
{
    private readonly Mock<ISimulatedTokenService> _tokenServiceMock;
    private readonly Mock<IOptionsSnapshot<AuthenticationSettings>> _authSettingsMock;
    private readonly Mock<ILogger<TokenController>> _loggerMock;
    private readonly TokenController _controller;

    public TokenControllerTests()
    {
        _tokenServiceMock = new Mock<ISimulatedTokenService>();
        _authSettingsMock = new Mock<IOptionsSnapshot<AuthenticationSettings>>();
        _loggerMock = new Mock<ILogger<TokenController>>();

        var settings = new AuthenticationSettings
        {
            Simulated = new SimulatedAuthSettings
            {
                Enabled = true,
                SigningKey = "test-signing-key-at-least-32-characters-long"
            }
        };
        _authSettingsMock.Setup(x => x.Value).Returns(settings);

        _controller = new TokenController(
            _tokenServiceMock.Object,
            _loggerMock.Object,
            _authSettingsMock.Object);
    }

    [Fact]
    public void ValidateToken_WithoutBearerPrefix_CallsServiceWithToken()
    {
        // Arrange
        var rawToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0In0.signature";
        var request = new TokenValidationRequest { Token = rawToken };
        
        _tokenServiceMock
            .Setup(x => x.ValidateToken(rawToken))
            .Returns(new TokenValidationResult { IsValid = true });

        // Act
        var result = _controller.ValidateToken(request);

        // Assert
        _tokenServiceMock.Verify(x => x.ValidateToken(rawToken), Times.Once);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TokenValidationResponse>(okResult.Value);
        Assert.True(response.IsValid);
    }

    [Fact]
    public void ValidateToken_WithBearerPrefix_StripsPrefix()
    {
        // Arrange
        var rawToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0In0.signature";
        var tokenWithBearer = $"Bearer {rawToken}";
        var request = new TokenValidationRequest { Token = tokenWithBearer };
        
        _tokenServiceMock
            .Setup(x => x.ValidateToken(rawToken))
            .Returns(new TokenValidationResult { IsValid = true });

        // Act
        var result = _controller.ValidateToken(request);

        // Assert
        // Verify that the service was called with the stripped token (no "Bearer " prefix)
        _tokenServiceMock.Verify(x => x.ValidateToken(rawToken), Times.Once);
        _tokenServiceMock.Verify(x => x.ValidateToken(tokenWithBearer), Times.Never);
    }

    [Fact]
    public void ValidateToken_WithBearerPrefixLowercase_StripsPrefix()
    {
        // Arrange
        var rawToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0In0.signature";
        var tokenWithBearer = $"bearer {rawToken}";
        var request = new TokenValidationRequest { Token = tokenWithBearer };
        
        _tokenServiceMock
            .Setup(x => x.ValidateToken(rawToken))
            .Returns(new TokenValidationResult { IsValid = true });

        // Act
        var result = _controller.ValidateToken(request);

        // Assert
        _tokenServiceMock.Verify(x => x.ValidateToken(rawToken), Times.Once);
    }

    [Fact]
    public void ValidateToken_WithEmptyToken_ReturnsBadRequest()
    {
        // Arrange
        var request = new TokenValidationRequest { Token = "" };

        // Act
        var result = _controller.ValidateToken(request);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        _tokenServiceMock.Verify(x => x.ValidateToken(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateToken_WithNullToken_ReturnsBadRequest()
    {
        // Arrange
        var request = new TokenValidationRequest { Token = null! };

        // Act
        var result = _controller.ValidateToken(request);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ValidateToken_WithOnlyBearerPrefix_ReturnsBadRequest()
    {
        // Arrange - "Bearer " with nothing after it
        var request = new TokenValidationRequest { Token = "Bearer " };

        // Act
        var result = _controller.ValidateToken(request);

        // Assert - Should return BadRequest since the token is empty after stripping
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        _tokenServiceMock.Verify(x => x.ValidateToken(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateToken_WithOnlyBearerWord_ReturnsBadRequest()
    {
        // Arrange - Just "Bearer" with no space or token
        var request = new TokenValidationRequest { Token = "Bearer" };

        // Act
        var result = _controller.ValidateToken(request);

        // Assert - Should return BadRequest
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        _tokenServiceMock.Verify(x => x.ValidateToken(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateToken_WhenSimulatedAuthDisabled_Returns403()
    {
        // Arrange
        var disabledSettings = new AuthenticationSettings
        {
            Simulated = new SimulatedAuthSettings { Enabled = false }
        };
        var disabledAuthSettingsMock = new Mock<IOptionsSnapshot<AuthenticationSettings>>();
        disabledAuthSettingsMock.Setup(x => x.Value).Returns(disabledSettings);

        var controller = new TokenController(
            _tokenServiceMock.Object,
            _loggerMock.Object,
            disabledAuthSettingsMock.Object);

        var request = new TokenValidationRequest { Token = "some-token" };

        // Act
        var result = controller.ValidateToken(request);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }
}
