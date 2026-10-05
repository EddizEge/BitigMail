using BitigMail.Engine.Models;
namespace BitigMail.Engine.Imap.Transfer;
public static class ImapAdvancedFilterPolicy
{
    public static CompiledMailFilter? ValidateFrozen(ImapTransferPlan plan) =>
        FrozenMailFilter.Validate(plan.AdvancedFilterCanonicalJson, plan.AdvancedFilterFingerprint, plan.AdvancedFilterUnknownCount);
}
