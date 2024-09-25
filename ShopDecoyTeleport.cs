using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopDecoyTeleport
{
    public class ShopDecoyTeleport : BasePlugin
    {
        public override string ModuleName => "[SHOP] Decoy Teleport";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "DecoyTp";
        public static JObject? JsonDecoyTeleport { get; private set; }
        private readonly PlayerDecoyTeleport[] playerDecoyTeleports = new PlayerDecoyTeleport[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/DecoyTeleport.json");
            if (File.Exists(configPath))
            {
                JsonDecoyTeleport = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonDecoyTeleport == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Телепорт на декой");

            foreach (var item in JsonDecoyTeleport.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerDecoyTeleports[playerSlot] = null!);
            RegisterListener<Listeners.OnClientConnected>(slot =>
            {
                playerDecoyTeleports[slot] = new PlayerDecoyTeleport(0, 0);
            });

            RegisterEventHandler<EventRoundStart>((@event, info) =>
            {
                for (var i = 0; i < playerDecoyTeleports.Length; i++)
                {
                    if (playerDecoyTeleports[i] != null)
                    {
                        playerDecoyTeleports[i].UsedTeleports = 0;
                    }
                }

                return HookResult.Continue;
            });

            RegisterEventHandler<EventDecoyFiring>(EventDecoyFiring);
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName,
            int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetTeleportsPerRound(uniqueName, out int teleportsPerRound))
            {
                playerDecoyTeleports[player.Slot].TeleportsPerRound = teleportsPerRound;
                playerDecoyTeleports[player.Slot].ItemId = itemId;
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'teleportsperround' in config!");
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetTeleportsPerRound(uniqueName, out int teleportsPerRound))
            {
                playerDecoyTeleports[player.Slot].TeleportsPerRound = teleportsPerRound;
                playerDecoyTeleports[player.Slot].ItemId = itemId;
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerDecoyTeleports[player.Slot].TeleportsPerRound = 0;
            playerDecoyTeleports[player.Slot].ItemId = 0;

            return HookResult.Continue;
        }

        private HookResult EventDecoyFiring(EventDecoyFiring @event, GameEventInfo info)
        {
            if (@event.Userid == null) return HookResult.Continue;

            var player = @event.Userid;
            var entityIndex = player.Index;

            if (playerDecoyTeleports[entityIndex] == null)
                return HookResult.Continue;

            var pDecoyFiring = @event;
            var playerPawn = player.PlayerPawn.Value;

            if (playerPawn == null) return HookResult.Continue;

            var teleportsPerRound = playerDecoyTeleports[entityIndex].TeleportsPerRound;
            if (playerDecoyTeleports[entityIndex].UsedTeleports >= teleportsPerRound && teleportsPerRound > 0)
                return HookResult.Continue;

            playerPawn.Teleport(new Vector(pDecoyFiring.X, pDecoyFiring.Y, pDecoyFiring.Z), playerPawn.AbsRotation,
                playerPawn.AbsVelocity);

            playerDecoyTeleports[entityIndex].UsedTeleports++;

            var decoyIndex = NativeAPI.GetEntityFromIndex(pDecoyFiring.Entityid);

            if (decoyIndex == IntPtr.Zero) return HookResult.Continue;

            new CBaseCSGrenadeProjectile(decoyIndex).Remove();

            return HookResult.Continue;
        }

        private bool TryGetTeleportsPerRound(string uniqueName, out int teleportsPerRound)
        {
            teleportsPerRound = 0;
            if (JsonDecoyTeleport != null && JsonDecoyTeleport.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["teleportsperround"] != null)
            {
                teleportsPerRound = (int)jsonItem["teleportsperround"]!;
                return true;
            }
            return false;
        }

        public record class PlayerDecoyTeleport(int TeleportsPerRound, int ItemId)
        {
            public int TeleportsPerRound { get; set; } = TeleportsPerRound;
            public int ItemId { get; set; } = ItemId;
            public int UsedTeleports { get; set; }
        };
    }
}