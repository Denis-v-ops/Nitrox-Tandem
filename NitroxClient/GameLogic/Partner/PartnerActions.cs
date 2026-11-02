using System.Collections.Generic;
using System.Linq;

namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Every registered <see cref="PartnerAction" />, highest priority first.
/// </summary>
public sealed class PartnerActions(IEnumerable<PartnerAction> actions)
{
    private readonly PartnerAction[] actions = actions.OrderByDescending(action => action.Priority).ToArray();

    public PartnerAction? FirstAvailable(PartnerHandTarget partner)
    {
        foreach (PartnerAction action in actions)
        {
            if (action.IsAvailable(partner))
            {
                return action;
            }
        }
        return null;
    }
}
