using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

public sealed class ConfirmationNonceServiceTests
{
    [Fact]
    public void A_fresh_nonce_validates_for_its_binding()
    {
        var nonces = new ConfirmationNonceService();
        var nonce = nonces.Issue(7, "logs-all", ConfirmationNonceService.PurposeExecute);

        Assert.True(nonces.Validate(7, "logs-all", ConfirmationNonceService.PurposeExecute, nonce));
    }

    [Fact]
    public void A_nonce_is_consumed_on_use_and_cannot_be_replayed()
    {
        var nonces = new ConfirmationNonceService();
        var nonce = nonces.Issue(7, "logs-all", ConfirmationNonceService.PurposeExecute);

        Assert.True(nonces.Validate(7, "logs-all", ConfirmationNonceService.PurposeExecute, nonce));
        Assert.False(nonces.Validate(7, "logs-all", ConfirmationNonceService.PurposeExecute, nonce));
    }

    [Fact]
    public void A_nonce_bound_to_a_different_project_or_action_fails()
    {
        var nonces = new ConfirmationNonceService();
        var other = nonces.Issue(9, "restart-map", ConfirmationNonceService.PurposeExecute);
        var nonce = nonces.Issue(7, "logs-all", ConfirmationNonceService.PurposeExecute);

        Assert.False(nonces.Validate(9, "logs-all", ConfirmationNonceService.PurposeExecute, nonce));
        Assert.False(nonces.Validate(7, "restart-map", ConfirmationNonceService.PurposeExecute, nonce));
        // Each failed attempt consumed the nonce — the correctly bound one still works.
        Assert.True(nonces.Validate(9, "restart-map", ConfirmationNonceService.PurposeExecute, other));
    }

    [Fact]
    public void A_nonce_issued_for_the_first_step_cannot_execute()
    {
        var nonces = new ConfirmationNonceService();
        var stepOne = nonces.Issue(7, "restart-map", ConfirmationNonceService.PurposeConfirm2);

        Assert.False(nonces.Validate(7, "restart-map", ConfirmationNonceService.PurposeExecute, stepOne));
    }

    [Fact]
    public void An_expired_nonce_fails()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var nonces = new ConfirmationNonceService(TimeSpan.FromMinutes(2), () => now);

        var nonce = nonces.Issue(7, "logs-all", ConfirmationNonceService.PurposeExecute);
        now = now.AddMinutes(3);

        Assert.False(nonces.Validate(7, "logs-all", ConfirmationNonceService.PurposeExecute, nonce));
    }

    [Fact]
    public void Unknown_or_missing_nonces_fail()
    {
        var nonces = new ConfirmationNonceService();

        Assert.False(nonces.Validate(7, "logs-all", ConfirmationNonceService.PurposeExecute, "forged"));
        Assert.False(nonces.Validate(7, "logs-all", ConfirmationNonceService.PurposeExecute, null));
    }
}
