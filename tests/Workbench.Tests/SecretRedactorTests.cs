using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Key_value_secrets_are_redacted()
    {
        Assert.Equal("MARIADB_PASSWORD=***", SecretRedactor.Redact("MARIADB_PASSWORD=s3cr3t!"));
        Assert.Equal("db_password=***", SecretRedactor.Redact("db_password=hunter2"));
        Assert.Equal("root-password: ***", SecretRedactor.Redact("root-password: p@ssw0rd"));
        // Only the value is redacted; the rest of the line survives.
        Assert.Equal("root-password: *** and more", SecretRedactor.Redact("root-password: p@ss and more"));
    }

    [Fact]
    public void Colon_and_json_styles_keep_their_shape()
    {
        Assert.Equal("password: ***", SecretRedactor.Redact("password: hunter2"));
        Assert.Equal("\"MARIADB_ROOT_PASSWORD\": \"***\"", SecretRedactor.Redact("\"MARIADB_ROOT_PASSWORD\": \"t0psecret\""));
        Assert.Equal("'api_key': '***'", SecretRedactor.Redact("'api_key': 'abcd1234'"));
    }

    [Fact]
    public void Secrets_embedded_in_log_lines_are_redacted_in_place()
    {
        var line = "2026-08-01 connect[1]: connecting with DB_PASSWORD=sup3rsecret retry=3";
        var redacted = SecretRedactor.Redact(line);

        Assert.DoesNotContain("sup3rsecret", redacted);
        Assert.Contains("DB_PASSWORD=***", redacted);
        Assert.Contains("retry=3", redacted);
    }

    [Theory]
    [InlineData("token=abc123")]
    [InlineData("access_token: xyz")]
    [InlineData("API-KEY=letmein")]
    [InlineData("client_secret = shh")]
    [InlineData("private_key=AAAA")]
    [InlineData("passwd: w00t")]
    public void Secret_like_keys_are_all_caught(string input)
    {
        Assert.DoesNotContain(input.Split('=', ':').Last().Trim(), SecretRedactor.Redact(input));
        Assert.Contains("***", SecretRedactor.Redact(input));
    }

    [Fact]
    public void Ordinary_output_is_left_untouched()
    {
        var output = """
            map  | Loading map data
            world| The world server is listening on port 54230
            database: ready for connections
            tokenizer=enabled
            """;

        Assert.Equal(output, SecretRedactor.Redact(output));
    }

    [Fact]
    public void Empty_values_and_empty_input_are_safe()
    {
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
        Assert.Equal("DB_PASSWORD=", SecretRedactor.Redact("DB_PASSWORD="));
    }
}
