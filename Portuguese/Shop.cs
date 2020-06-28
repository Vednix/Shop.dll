﻿using System;
using System.IO;
using System.Collections.Generic;
using TShockAPI;
using Terraria;
using Newtonsoft.Json;
using TerrariaApi.Server;
using Wolfje.Plugins.SEconomy;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using TShockAPI.DB;
using System.Data;
using Microsoft.Xna.Framework;
using Wolfje.Plugins.SEconomy.Journal;

namespace Shop
{
	[ApiVersion(2, 1)]
	public class Shop : TerrariaPlugin
	{
		public static IDbConnection ShopDB;
		private Config config;
		public override Version Version
		{
			get { return new Version("1.3.2.0"); }
		}
		public override string Name
		{
			get { return "Shop"; }
		}
		public override string Author
		{
			get { return "RYH & Little Update/Mobile port by Vednix"; }
		}
		public override string Description
		{
			get { return "Shop plugin allow players buy item(s). Require SEconomy."; }
		}
		public Shop(Main game) : base(game)
		{
			Order = 1;
		}
		public override void Initialize()
		{
			Commands.ChatCommands.Add(new Command(Buy, "shop", "buy"));
			Commands.ChatCommands.Add(new Command("shop.reload", ReloadConfig, "reloadshop"));
			ReadConfig();

			switch (TShock.Config.StorageType.ToLower())
			{
				case "mysql":
					string[] host = TShock.Config.MySqlHost.Split(':');
					ShopDB = new MySqlConnection()
					{
						ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
							host[0],
							host.Length == 1 ? "3306" : host[1],
							TShock.Config.MySqlDbName,
							TShock.Config.MySqlUsername,
							TShock.Config.MySqlPassword)
					};
					break;
				case "sqlite":
					string sql = Path.Combine(TShock.SavePath, "ShopHistory.sqlite");
					ShopDB = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
					break;
			}
			SqlTableCreator sqlcreator = new SqlTableCreator(ShopDB,
				ShopDB.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
			sqlcreator.EnsureTableStructure(new SqlTable("ShopHistory",
				new SqlColumn("Time", MySqlDbType.Int32),
				new SqlColumn("Account", MySqlDbType.VarChar) { Length = 50 },
				new SqlColumn("ItemName", MySqlDbType.String, 70),
				new SqlColumn("WorldID", MySqlDbType.Int32),
				new SqlColumn("Price", MySqlDbType.String, 100)));
		}
		private static string ItemToTag(SimpleItem args)
		{
			string ret = ((args.prefix != 0) ? "[i/p" + args.prefix : "[i");
			ret = (args.stack != 1) ? ret + "/s" + args.stack : ret;
			ret = ret + ":" + args.netID + "]";
			if (args.netID == 0) return "";
			return ret;
		}
		private void Buy(CommandArgs args)
		{
			if ((args.Parameters.Count < 1))
			{
				args.Player.SendErrorMessage("[c/FF00FF:TerraStore]\n" +
											"Digite [c/ffd700:/shop menu] para ver o catálogo de Itens.\n" +
											"Digite [c/ffd700:/shop <nome no catálogo>] para comprar um Item.");
				return;
			}
			if (args.Parameters[0] == "menu")
			{
				int pageNumber = 1;
				if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
					return;
				var lines = new List<string> { };
				foreach (var gooda in config.All)
				{
					string total = $"* [c/ffa500:{gooda.DisplayName}]: ";
					foreach (var item in gooda.IncludeItems)
					{
						total = $"{total} {ItemToTag(item)}";
					}
					total = $"{total} > [c/50c878:{Money.Parse(Convert.ToString(gooda.Price))}]";

					if (args.Player.Group.HasPermission(gooda.RequirePermission))
					{
						lines.Add(total);
					}
					else if (!config.HideUnavailableGoods)
					{
						string perm = args.Player.Group.HasPermission(gooda.RequirePermission) ? "[c/84d184:(Disponível)]" : "[c/ff0000:(Em Falta)]";
						lines.Add($"{perm} {total}");
					}
				}
				PaginationTools.SendPage(args.Player, pageNumber, lines,
										 new PaginationTools.Settings
										 {
											 HeaderFormat = "TerraStore Menu ({0}/{1}):",
											 FooterFormat = "Digite [c/ffd700:{0}shop menu {{0}}] para ver mais.".SFormat(Commands.Specifier),
											 HeaderTextColor = Color.Magenta,
											 FooterTextColor = Color.ForestGreen,
											 LineTextColor = Color.LightGray,
											 MaxLinesPerPage = 3
										 }
										);
				return;
			}
			var Find = new Goods();
			bool FindSuccess = false;
			string medname = args.Parameters[0];
			for (int i = 1; i < args.Parameters.Count; i++)
			{
				medname = $"{medname} {args.Parameters[i]}";
			}

			foreach (var i1 in config.All)
			{
				if (medname.ToLower() == i1.DisplayName.ToLower())
				{
					Find = i1;
					FindSuccess = true;
				}
			}

			if ((!FindSuccess) || (!args.Player.Group.HasPermission(Find.RequirePermission) && config.HideUnavailableGoods))
			{
				args.Player.SendErrorMessage("Não foi possível encontrar o Item especificado na loja. Digite [c/ffd700:/shop menu] para ver o catálogo.");
				return;
			}
			if (!args.Player.Group.HasPermission(Find.RequirePermission))
			{
				args.Player.SendErrorMessage("Este Item está em falta!");
				return;
			}

			var UsernameBankAccount = SEconomyPlugin.Instance.GetBankAccount(args.Player.Name);
			var playeramount = UsernameBankAccount.Balance;
			Money amount = -Find.Price;
			Money amount2 = Find.Price;
			var amount3 = Money.Parse(Convert.ToString(amount2));
			var Journalpayment = BankAccountTransferOptions.AnnounceToReceiver;

			if (args.Player == null || UsernameBankAccount == null)
			{
				args.Player.SendErrorMessage("Ocorreu um erro recuperando os dados de [c/00594F:{0}].", args.Player.Name);
				return;
			}

			if (playeramount < amount2)
			{
				args.Player.SendErrorMessage($"O preço do Item [c/ffa500:'{Find.DisplayName}'] é [c/50c878:'{Money.Parse(Convert.ToString(Find.Price))}'], mas você possui apenas [c/ff0000:{UsernameBankAccount.Balance}] em sua conta.");
				return;
			}

			if (!args.Player.InventorySlotAvailable)
			{
				args.Player.SendErrorMessage("Your inventory is full.");
				return;
			}

			SEconomyPlugin.Instance.WorldAccount.TransferToAsync(UsernameBankAccount, amount,
																Journalpayment, string.Format("Comprar {0}", Find.DisplayName),
																 string.Format("Buying " + Find.DisplayName));
			args.Player.SendMessage($"Você comprou [c/ffa500:'{Find.DisplayName}'] por [c/50c878{amount3}].", Color.White);
			TShock.Log.ConsoleInfo("{0} has paid {2} to buy {1}.", args.Player.Name, Find.DisplayName, amount2);
			foreach (var item in Find.IncludeItems)
			{
				var q = new Item();
				q.netDefaults(item.netID);
				q.stack = item.stack;
				q.Prefix(item.prefix);
				args.Player.GiveItemCheck(q.type, q.Name, q.width, q.height, q.stack, q.prefix);
			}
			/* Debug? */ /*var num = */
			ShopDB.Query("INSERT INTO ShopHistory (Time, Account, ItemName, WorldID, price) VALUES (@0, @1, @2, @3, @4);", DateTime.Now, args.Player.Name, Find.DisplayName, Main.worldID, amount3);
		}

		private void CreateConfig()
		{
			string filepath = Path.Combine(TShock.SavePath, "Shop.json");
			try
			{
				using (var stream = new FileStream(filepath, FileMode.Create, FileAccess.Write, FileShare.Write))
				{
					using (var sr = new StreamWriter(stream))
					{
						config = new Config(1);
						var configString = JsonConvert.SerializeObject(config, Formatting.Indented);
						sr.Write(configString);
					}
					stream.Close();
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.Message);
			}
		}
		private bool ReadConfig()
		{
			string filepath = Path.Combine(TShock.SavePath, "Shop.json");
			try
			{
				if (File.Exists(filepath))
				{
					using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						using (var sr = new StreamReader(stream))
						{
							var configString = sr.ReadToEnd();
							config = JsonConvert.DeserializeObject<Config>(configString);
							foreach (var element in config.All)
							{
								foreach (var element2 in element.IncludeItems)
								{
									element2.Full();
								}
							}
						}
						stream.Close();
					}
					return true;
				}
				else
				{
					TShock.Log.ConsoleError("Create new config file for Shop.");
					CreateConfig();
					return false;
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.Message);
			}
			return false;
		}
		private void ReloadConfig(CommandArgs args)
		{
			if (ReadConfig())
			{
				args.Player.SendInfoMessage("Load success.");
				return;
			}
			args.Player.SendErrorMessage("Load fails. Check log for more details.");
		}
	}
	public class Config
	{
		public bool HideUnavailableGoods;
		public List<Goods> All;
		public Config()
		{ }
		public Config(int a)
		{
			HideUnavailableGoods = true;
			All = new List<Goods> { new Goods(1), new Goods(2) };
		}
	}
	public class Goods
	{
		public string DisplayName = "";
		public string RequirePermission = "";
		public int Price = 0;
		public List<SimpleItem> IncludeItems = new List<SimpleItem> { };
		public Goods() { }
		public Goods(int a)
		{
			if (a == 1)
			{
				var i1 = new SimpleItem(2760);
				var i2 = new SimpleItem(2761);
				var i3 = new SimpleItem(2762);
				DisplayName = "ExampleNebula";
				RequirePermission = "shop.buy";
				Price = 500000;
				IncludeItems = new List<SimpleItem> { i1, i2, i3 };
			}
			if (a == 2)
			{
				DisplayName = "Example2";
				Price = 20;
				for (int i = 0; i < 10; i++)
				{
					IncludeItems.Add(new SimpleItem(i + 2702));
				}
			}
		}
	}
	public class SimpleItem
	{
		public int netID = 0;
		public int stack = 1;
		public int prefix = 0;
		public string name = "";
		public SimpleItem() { }
		public SimpleItem(int a)
		{
			this.name = TShockAPI.Utils.Instance.GetItemByIdOrName(a.ToString())[0].Name;
			this.netID = TShockAPI.Utils.Instance.GetItemByIdOrName(a.ToString())[0].type;
		}
		public void Full()
		{
			this.netID = TShockAPI.Utils.Instance.GetItemByIdOrName((netID != 0) ? netID.ToString() : name)[0].type;
			this.name = TShockAPI.Utils.Instance.GetItemByIdOrName(netID.ToString())[0].Name;
			//this.stack = this.stack;
			//this.prefix = this.prefix;
		}
	}
}