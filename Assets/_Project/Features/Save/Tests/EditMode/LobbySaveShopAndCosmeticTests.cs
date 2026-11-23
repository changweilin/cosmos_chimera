using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests.EditMode
{
    public sealed class LobbySaveShopAndCosmeticTests
    {
        private static readonly Progression P = Progression.FromJson(
            File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));
        [Test]
        public void Gold_and_ng_plus_cosmetics_round_trip_through_json()
        {
            var save = new LobbySave();
            save.AddGold(250);
            Assert.That(save.TrySpendGold(100), Is.True);
            Assert.That(save.Gold, Is.EqualTo(150));
            Assert.That(save.TrySpendGold(200), Is.False);
            Assert.That(save.Gold, Is.EqualTo(150));

            // Cosmetic override requires NgPlusUnlocked
            Assert.Throws<ContractException>(() =>
                save.SetCosmeticAppearance("chr_roen", "chr_yeh_chengxi", "chr_amemori_sumiha"));

            save.CompleteRivalQuest();
            Assert.That(save.NgPlusUnlocked, Is.True);

            save.SetCosmeticAppearance("chr_roen", "chr_yeh_chengxi", "chr_amemori_sumiha");
            Assert.That(save.SaveCosmeticFavorite(new LobbySave.CosmeticFavoritePreset(
                "Fav 1", "chr_roen", "chr_yeh_chengxi", "chr_amemori_sumiha")), Is.True);

            string json = save.ToJson();
            LobbySave reloaded = LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile);

            Assert.That(reloaded.Gold, Is.EqualTo(150));
            Assert.That(reloaded.NgPlusUnlocked, Is.True);
            Assert.That(reloaded.CosmeticCharacterId, Is.EqualTo("chr_roen"));
            Assert.That(reloaded.CosmeticOutfitCharacterId, Is.EqualTo("chr_yeh_chengxi"));
            Assert.That(reloaded.CosmeticAccessoryCharacterId, Is.EqualTo("chr_amemori_sumiha"));
            Assert.That(reloaded.CosmeticFavorites.Count, Is.EqualTo(1));
            Assert.That(reloaded.CosmeticFavorites[0].Name, Is.EqualTo("Fav 1"));
        }

        [Test]
        public void Cosmetic_favorites_enforce_max_limit_and_allow_removal()
        {
            var save = new LobbySave();
            for (int i = 0; i < LobbySave.MaxCosmeticFavorites; i++)
            {
                Assert.That(save.SaveCosmeticFavorite(new LobbySave.CosmeticFavoritePreset(
                    $"Preset {i}", "chr_roen", "chr_roen", "chr_roen")), Is.True);
            }

            Assert.That(save.SaveCosmeticFavorite(new LobbySave.CosmeticFavoritePreset(
                "Overflow", "chr_roen", "chr_roen", "chr_roen")), Is.False);

            save.RemoveCosmeticFavorite(0);
            Assert.That(save.CosmeticFavorites.Count, Is.EqualTo(LobbySave.MaxCosmeticFavorites - 1));
            Assert.That(save.SaveCosmeticFavorite(new LobbySave.CosmeticFavoritePreset(
                "Replacement", "chr_roen", "chr_roen", "chr_roen")), Is.True);
        }
    }
}
