namespace Birchpad.Core.Menu;

/// <summary>What one of the nine keys shows on a menu page.</summary>
public enum KeyRole
{
    Blank,
    Item,
    Back,
    More,
}

/// <summary>One key: its role and, for an item, the index into the page's item list.</summary>
public readonly record struct KeySlot(KeyRole Role, int ItemIndex = -1);

/// <summary>
/// Places items on the 3x3 keypad: the top-left key is always Back, where the keypad's own folders put it,
/// the other eight hold items, and a list longer than eight gives the bottom-right key to More, which pages through the rest.
/// </summary>
public static class MenuLayout
{
    public const int KeyCount = 9;
    public const int BackKey = 0;
    public const int MoreKey = 8;

    private static readonly int[] ItemKeys = [1, 2, 3, 4, 5, 6, 7, 8];

    public static int PageCount(int itemCount) =>
        itemCount <= ItemKeys.Length ? 1 : (itemCount + ItemKeys.Length - 2) / (ItemKeys.Length - 1);

    /// <summary>Lays out one page; <paramref name="page"/> wraps, so More on the last page returns to the first.</summary>
    public static KeySlot[] Arrange(int itemCount, int page)
    {
        var slots = new KeySlot[KeyCount];
        slots[BackKey] = new KeySlot(KeyRole.Back);

        var overflow = itemCount > ItemKeys.Length;
        var perPage = overflow ? ItemKeys.Length - 1 : ItemKeys.Length;
        var pages = PageCount(itemCount);
        page = ((page % pages) + pages) % pages;

        for (var i = 0; i < perPage; i++)
        {
            var index = (page * perPage) + i;
            if (index < itemCount)
            {
                slots[ItemKeys[i]] = new KeySlot(KeyRole.Item, index);
            }
        }

        if (overflow)
        {
            slots[MoreKey] = new KeySlot(KeyRole.More);
        }

        return slots;
    }
}
