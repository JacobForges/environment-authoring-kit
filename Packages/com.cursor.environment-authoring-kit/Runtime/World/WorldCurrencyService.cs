using System;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Copper-based wallet with 100:1 tier conversion (Cu → Ag → Au → Pt).</summary>
    public static class WorldCurrencyService
    {
        public const long CopperPerSilver = 100L;
        public const long CopperPerGold = 10_000L;
        public const long CopperPerPlatinum = 1_000_000L;

        public static void AddCopper(PlayerCardData card, long copper)
        {
            if (card == null || copper <= 0)
                return;

            card.currency ??= new CurrencyWalletData();
            card.currency.copper += copper;
            Normalize(card);
        }

        public static void AddTier(PlayerCardData card, CurrencyType tier, long amount)
        {
            if (card == null || amount <= 0)
                return;

            var copper = tier switch
            {
                CurrencyType.Copper => amount,
                CurrencyType.Silver => amount * CopperPerSilver,
                CurrencyType.Gold => amount * CopperPerGold,
                CurrencyType.Platinum => amount * CopperPerPlatinum,
                _ => amount,
            };
            AddCopper(card, copper);
        }

        public static void Normalize(PlayerCardData card)
        {
            if (card?.currency == null)
                return;

            var c = card.currency;
            var total = c.copper
                        + c.silver * CopperPerSilver
                        + c.gold * CopperPerGold
                        + c.platinum * CopperPerPlatinum;

            c.platinum = total / CopperPerPlatinum;
            total %= CopperPerPlatinum;
            c.gold = total / CopperPerGold;
            total %= CopperPerGold;
            c.silver = total / CopperPerSilver;
            total %= CopperPerSilver;
            c.copper = total;
        }

        public static long TotalCopper(CurrencyWalletData wallet)
        {
            if (wallet == null)
                return 0;

            return wallet.copper
                   + wallet.silver * CopperPerSilver
                   + wallet.gold * CopperPerGold
                   + wallet.platinum * CopperPerPlatinum;
        }

        public static bool TrySpendCopper(PlayerCardData card, long copperCost)
        {
            if (card == null || copperCost <= 0)
                return true;

            Normalize(card);
            if (TotalCopper(card.currency) < copperCost)
                return false;

            var remaining = TotalCopper(card.currency) - copperCost;
            card.currency.copper = 0;
            card.currency.silver = 0;
            card.currency.gold = 0;
            card.currency.platinum = 0;
            AddCopper(card, remaining);
            return true;
        }
    }
}
