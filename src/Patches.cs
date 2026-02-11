using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;

namespace PastelParadeAccess;

internal static partial class Patches
{
	public static bool AutoSpeakEnabled = true;
	private static bool _earlyPatchesApplied;
	private static bool _fullPatchesApplied;
	private static bool _patchedEarlyWorldMapParentStart;
	private static bool _patchedEarlyMusicSelectUpdateUI;
	private static bool _patchedEarlyWorldMapAreaMoveBegin;
	private static bool _patchedEarlyGameVersionStart;
	private static bool _patchedEarlyGameTransitionViewSetUp;
	private static bool _patchedEarlyTitleStateTitleText;
	private static bool _patchedEarlyUiLoadingStateText;
	private static bool _patchedEarlyDebugPanelAddText;
	private static string _lastTalkLine;
	private static string _lastTutorialLeftCount;
	private static string _lastTimingDiffText;
	private static DateTime _lastTimingDiffAt = DateTime.MinValue;
	private static string _lastNovelLineRequested;
	private static DateTime _lastNovelLineRequestedAt = DateTime.MinValue;
	private static string _lastWorldMapName;
	private static DateTime _lastWorldMapNameAt = DateTime.MinValue;
	private static string _lastMusicSelectHeader;
	private static DateTime _lastMusicSelectHeaderAt = DateTime.MinValue;
	private static string _lastAreaMoveText;
	private static DateTime _lastAreaMoveTextAt = DateTime.MinValue;
	private static string _lastGameVersionText;
	private static DateTime _lastGameVersionAt = DateTime.MinValue;
	private static string _lastGameTransitionMusic;
	private static DateTime _lastGameTransitionMusicAt = DateTime.MinValue;
	private static string _lastGameTransitionBundle;
	private static DateTime _lastGameTransitionBundleAt = DateTime.MinValue;
	private static string _lastResultHeader;
	private static DateTime _lastResultHeaderAt = DateTime.MinValue;
	private static string _lastTitleHeaderText;
	private static DateTime _lastTitleHeaderAt = DateTime.MinValue;
	// (removed) productName fallback for title
	private static string _lastTitleStateTitleText;
	private static DateTime _lastTitleStateTitleAt = DateTime.MinValue;
	private static string _lastUiLoadingBundle;
	private static DateTime _lastUiLoadingBundleAt = DateTime.MinValue;
	private static readonly Regex _tmpRichTextTagRegex = new Regex("<[^>]*>", RegexOptions.Compiled);
	private static readonly HashSet<int> _tutorialTalkPanelIds = new HashSet<int>();
	private static DateTime _tutorialTalkPanelIdsRefreshedAt = DateTime.MinValue;

	// WorldMap interactables cycle ([ / ])
	private static int _worldMapCycleCurrentInteractableId;
	private static DateTime _worldMapCycleLastAt = DateTime.MinValue;

	// Hub (收藏/相簿等物件互動) cycle ([ / ])
	private static int _hubCycleCurrentInteractableId;
	private static DateTime _hubCycleLastAt = DateTime.MinValue;
	private static string _lastHubInteractableText;
	private static DateTime _lastHubInteractableTextAt = DateTime.MinValue;

	// Timing setting slider: map slider -> its value text (ms)
	private static readonly Dictionary<int, object> _timingTextBySliderId = new Dictionary<int, object>();
	private static string _lastTimingSettingText;
	private static DateTime _lastTimingSettingTextAt = DateTime.MinValue;

	// Display/Sound settings: map slider -> its visible value text
	private static readonly Dictionary<int, object> _settingsValueTextBySliderId = new Dictionary<int, object>();
	private static string _lastSettingsValueText;
	private static DateTime _lastSettingsValueTextAt = DateTime.MinValue;
	private static readonly HashSet<int> _timingTmpTextIds = new HashSet<int>();
	private static readonly HashSet<int> _settingsTmpTextIds = new HashSet<int>();
	private static string _lastTimingHintText;
	private static DateTime _lastTimingHintAt = DateTime.MinValue;
	private static object _timingHintCaptureRootGo;
	private static DateTime _timingHintCaptureUntil = DateTime.MinValue;
	private static int _timingHintCaptureValueTextId;
	private static object _startupCaptureRootGo;
	private static DateTime _startupCaptureUntil = DateTime.MinValue;
	private static object _loadingCaptureRootGo;
	private static DateTime _loadingCaptureUntil = DateTime.MinValue;
	
	// Settings (general pages): Prefab/Localize texts may not go through TMP_Text.set_text
	private static object _settingsCaptureRootGo;
	private static DateTime _settingsCaptureUntil = DateTime.MinValue;
	private static DateTime _settingsLastHeuristicScanAt = DateTime.MinValue;
	private static DateTime _startupLastHeuristicScanAt = DateTime.MinValue;
	private static DateTime _loadingLastHeuristicScanAt = DateTime.MinValue;
	private static readonly HashSet<int> _startupTmpTextIds = new HashSet<int>();
	private static readonly HashSet<int> _loadingTmpTextIds = new HashSet<int>();
	private static string _lastStartupSpoken;
	private static DateTime _lastStartupSpokenAt = DateTime.MinValue;
	private static string _lastLoadingSpoken;
	private static DateTime _lastLoadingSpokenAt = DateTime.MinValue;

	// 遊戲選擇介面：
	// - Hub 遊戲清單：UIHubGameRow 的 focus 會呼叫 HubService.SetPreview(...)
	// - Hub 歌曲視聽：UIHubSoundTestRow 的 focus 也會呼叫 HubService.SetPreview(...)
	// - MusicSelect：MusicSelectState 在選取時會設定 MusicSelectService.DetailRp.Value
	private static object _musicSelectDetailRpInstance;

	private static string StripTmpRichText(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return s;
		// 注意：原本用 Regex "<[^>]*>" 會把「一般文字」也刪掉（例如 "<It’s ...>" 這種括號式句子）。
		// 這裡改成「只移除看起來像 TMP rich-text tag 的片段」。
		try
		{
			bool IsTag(string inner)
			{
				inner = (inner ?? "").Trim();
				if (inner.Length == 0) return false;
				if (inner[0] == '/') return true; // closing tag
				if (inner.IndexOf('=') >= 0) return true; // attributes (color=#, size=, sprite=...)
				// first token name
				var name = inner;
				int sp = name.IndexOf(' ');
				if (sp > 0) name = name.Substring(0, sp);
				name = name.Trim().ToLowerInvariant();
				switch (name)
				{
					case "b":
					case "i":
					case "u":
					case "s":
					case "color":
					case "size":
					case "sprite":
					case "link":
					case "mark":
					case "voffset":
					case "font":
					case "material":
					case "alpha":
					case "align":
					case "pos":
					case "cspace":
					case "mspace":
					case "nobr":
					case "nowrap":
					case "width":
					case "indent":
					case "line-height":
					case "line-indent":
					case "rotate":
					case "scale":
						return true;
				}
				return false;
			}

			var sb = new System.Text.StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c != '<')
				{
					sb.Append(c);
					continue;
				}

				int end = s.IndexOf('>', i + 1);
				if (end < 0)
				{
					sb.Append(c);
					continue;
				}

				string inner = s.Substring(i + 1, end - i - 1);
				if (IsTag(inner))
				{
					i = end; // skip tag
					continue;
				}

				// keep as normal text (e.g. "<It’s ...>")
				sb.Append(s, i, end - i + 1);
				i = end;
			}
			return sb.ToString();
		}
		catch
		{
			// fallback to previous behavior
			return _tmpRichTextTagRegex.Replace(s, "");
		}
	}

	public static void ApplyPatches()
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Expected O, but got Unknown
		//IL_03be: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cb: Expected O, but got Unknown
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Expected O, but got Unknown
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0303: Expected O, but got Unknown
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_0145: Expected O, but got Unknown
		//IL_01c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Expected O, but got Unknown
		//IL_024a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0257: Expected O, but got Unknown
		if (_fullPatchesApplied) return;
		MelonLogger.Msg("TolkExporter: ApplyPatches() start");
		try
		{
			// 這個遊戲有機會使用 asmdef 分拆成多個組件，不一定都在 Assembly-CSharp。
			// 因此不要硬綁 Assembly-CSharp，改用 AccessTools.TypeByName 直接從已載入組件解析型別。
			HarmonyLib.Harmony val = new HarmonyLib.Harmony("com.assistant.tolkexporter");

			// 重要：早期 patch 可能在組件尚未載入時跑過一次導致「type 找不到就漏掛」。
			// 因此 ApplyPatches 也要再跑一次 TryPatchEarly（用旗標避免重複 patch）。
			TryPatchEarly(val);

			Type type = AccessTools.TypeByName("PastelParade.UIHubTips");
			if (type != null)
			{
				MethodInfo method = type.GetMethod("SetUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo method2 = typeof(Patches).GetMethod("UIHubTips_SetUp_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (method != null && method2 != null)
				{
					val.Patch((MethodBase)method, (HarmonyMethod)null, new HarmonyMethod(method2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.UIHubTips.SetUp");
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.UIHubTips.SetUp not found or postfix missing");
				}
			}
			else
			{
				MelonLogger.Msg("TolkExporter: PastelParade.UIHubTips type not found");
			}
			Type type2 = AccessTools.TypeByName("PastelParade.UIWorldMapDetail");
			if (type2 != null)
			{
				MethodInfo method3 = type2.GetMethod("SetUpDetail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo method4 = typeof(Patches).GetMethod("UIWorldMapDetail_SetUpDetail_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (method3 != null && method4 != null)
				{
					val.Patch((MethodBase)method3, (HarmonyMethod)null, new HarmonyMethod(method4), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.UIWorldMapDetail.SetUpDetail");
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.UIWorldMapDetail.SetUpDetail not found or postfix missing");
				}
			}
			else
			{
				MelonLogger.Msg("TolkExporter: PastelParade.UIWorldMapDetail type not found");
			}

			// 地圖操作狀態：從劇情/小說回到地圖時，WorldMapMoveState.OnStateBegin 會再次進入
			// 但 WorldMapParentUI.Start 不會重跑，所以這裡補唸一次地圖名稱（讀 UI 上實際顯示的 _worldName.text）
			try
			{
				Type tMove = AccessTools.TypeByName("PastelParade.WorldMapMoveState");
				if (tMove != null)
				{
					MethodInfo mBegin = tMove.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("WorldMapMoveState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mBegin != null && post != null)
					{
						val.Patch((MethodBase)mBegin, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.WorldMapMoveState.OnStateBegin (map name re-speak)");
					}

					// 地圖交互點快速切換：在 WorldMapMoveState.OnStateUpdate 監聽 [ / ] 並將角色(指針)移動到目標交互點
					MethodInfo mUpdate = tMove.GetMethod("OnStateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo postUpdate = typeof(Patches).GetMethod("WorldMapMoveState_OnStateUpdate_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mUpdate != null && postUpdate != null)
					{
						val.Patch((MethodBase)mUpdate, null, new HarmonyMethod(postUpdate), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.WorldMapMoveState.OnStateUpdate (cycle interactables)");
					}
				}
			}
			catch { }

			// Hub 互動（相簿/收藏類）：HubInteractState 內用 HubCursor 移動指針，不走 UI Selectable
			// 這裡補 [ / ] 循環移動到下一個 HubInteractable 並朗讀它的文字（可直接從 TMP_Text 取）
			try
			{
				Type tHubInteract = AccessTools.TypeByName("PastelParade.HubInteractState");
				if (tHubInteract != null)
				{
					MethodInfo mUpdate = tHubInteract.GetMethod("OnStateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("HubInteractState_OnStateUpdate_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mUpdate != null && post != null)
					{
						val.Patch((MethodBase)mUpdate, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.HubInteractState.OnStateUpdate (cycle hub interactables)");
					}
				}
			}
			catch { }

			// HubInteractable focus：讀取該物件的顯示文字（相簿/唱片/小說/收音機等）
			try
			{
				Type tHubIt = AccessTools.TypeByName("PastelParade.HubInteractable");
				if (tHubIt != null)
				{
					MethodInfo m = tHubIt.GetMethod("OnFocus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("HubInteractable_OnFocus_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && post != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.HubInteractable.OnFocus (speak)");
					}
				}
			}
			catch { }

			// 設定 - タイミング調整：slider 變更時用 _timingText（ms）朗讀（原始碼：UITimingSettingState.OnStateBegin 裡會更新 _timingText.text）
			try
			{
				Type tTiming = AccessTools.TypeByName("PastelParade.UITimingSettingState");
				if (tTiming != null)
				{
					MethodInfo mBegin = tTiming.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo postBegin = typeof(Patches).GetMethod("UITimingSettingState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mBegin != null && postBegin != null)
					{
						val.Patch((MethodBase)mBegin, null, new HarmonyMethod(postBegin), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.UITimingSettingState.OnStateBegin (timing slider speak)");
					}

					MethodInfo mEnd = tTiming.GetMethod("OnStateEnd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo postEnd = typeof(Patches).GetMethod("UITimingSettingState_OnStateEnd_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mEnd != null && postEnd != null)
					{
						val.Patch((MethodBase)mEnd, null, new HarmonyMethod(postEnd), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.UITimingSettingState.OnStateEnd (timing slider clear)");
					}
				}
			}
			catch { }

			// 設定 - 解析度/音量：左右鍵調整時直接讀取畫面上對應的 text（避免 selection gating 擋住）
			try
			{
				PatchSettingsValueTextMapping(val, "PastelParade.UIDisplaySettings",
					new[] { "_renderScaleSlider", "_resolutionSlider", "_maxFPSSlider" },
					new[] { "_scaleText", "_resolutionText", "_maxFPSText" });
				PatchSettingsValueTextMapping(val, "PastelParade.UISoundSettingsState",
					new[] { "_seSlider", "_bgmSlider" },
					new[] { "_seText", "_bgmText" });
			}
			catch { }
			Type type3 = AccessTools.TypeByName("PastelParade.UIHubNovelRow");
			if (type3 != null)
			{
				MethodInfo method5 = type3.GetMethod("SetData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo method6 = typeof(Patches).GetMethod("UIHubNovelRow_SetData_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (method5 != null && method6 != null)
				{
					val.Patch((MethodBase)method5, (HarmonyMethod)null, new HarmonyMethod(method6), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.UIHubNovelRow.SetData");
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.UIHubNovelRow.SetData not found or postfix missing");
				}
			}
			else
			{
				MelonLogger.Msg("TolkExporter: PastelParade.UIHubNovelRow type not found");
			}

			// 主選單/Hub：遊戲列表列（對照 Pastel☆Parade 原始碼：UIHubGameRow.SetData(GameDetailSo)）
			Type typeGameRow = AccessTools.TypeByName("PastelParade.UIHubGameRow");
			if (typeGameRow != null)
			{
				MethodInfo mSetData = typeGameRow.GetMethod("SetData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("UIHubGameRow_SetData_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mSetData != null && mPost != null)
				{
					val.Patch((MethodBase)mSetData, (HarmonyMethod)null, new HarmonyMethod(mPost), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.UIHubGameRow.SetData");
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.UIHubGameRow.SetData not found or postfix missing");
				}
			}
			else
			{
				MelonLogger.Msg("TolkExporter: PastelParade.UIHubGameRow type not found");
			}

			// Hub 遊戲選擇：在 UIHubGameRow focus 時，UIHubGame 會呼叫 HubService.SetPreview(gameDetail.Thumbnail, "")
			// 對照原始碼：Pastel☆Parade/UIHubGame.cs UpdateUI() -> uIHubGameRow.OnFocus.Subscribe(...)
			try
			{
				Type typeHubService = AccessTools.TypeByName("PastelParade.HubService");
				if (typeHubService != null)
				{
					MethodInfo m = typeHubService.GetMethod("SetPreview", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("HubService_SetPreview_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(mPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.HubService.SetPreview (game focus)");
					}
				}
			}
			catch (Exception exHubPreview)
			{
				MelonLogger.Warning("TolkExporter: patch HubService.SetPreview exception: " + exHubPreview);
			}

			// 同一個 state 內切換 tab/分類時：UIHubXXX.UpdateUI 會清空/重建 rows，但不觸發 state change。
			// 依使用者提供原始碼流程：必須在 UpdateUI 結尾重新觸發一次焦點朗讀。
			try
			{
				PatchUpdateUiPostfix(val, "PastelParade.UIHubGame", "UIHubGame_UpdateUI_Postfix");
				PatchUpdateUiPostfix(val, "PastelParade.UIHubNovel", "UIHubNovel_UpdateUI_Postfix");
				PatchUpdateUiPostfix(val, "PastelParade.UIHubSoundTest", "UIHubSoundTest_UpdateUI_Postfix");
				PatchUpdateUiPostfix(val, "PastelParade.UIGallery", "UIGallery_UpdateUI_Postfix");
			}
			catch { }

			// MusicSelect 選取：MusicSelectState 在 OnSelect 時設定 _service.DetailRp.Value = detail.GameDetail;
			// 對照原始碼：Pastel☆Parade/MusicSelectState.cs (OnStateBegin 註冊 OnSelect)
			try
			{
				Type typeMusicSelectService = AccessTools.TypeByName("PastelParade.MusicSelectService");
				if (typeMusicSelectService != null)
				{
					MethodInfo mGet = typeMusicSelectService.GetMethod("get_DetailRp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mGetPost = typeof(Patches).GetMethod("MusicSelectService_get_DetailRp_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mGet != null && mGetPost != null)
					{
						val.Patch((MethodBase)mGet, null, new HarmonyMethod(mGetPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.MusicSelectService.get_DetailRp");
					}
				}

				// patch UniRx.ReactiveProperty<GameDetailSo>.set_Value
				Type gameDetailType = AccessTools.TypeByName("PastelParade.GameDetailSo");
				if (gameDetailType != null)
				{
					MethodInfo post = typeof(Patches).GetMethod("ReactivePropertyGameDetail_set_Value_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (post != null)
					{
						foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
						{
							Type[] types;
							try { types = asm.GetTypes(); } catch { continue; }
							foreach (var t in types)
							{
								try
								{
									if (t == null) continue;
									if (!t.IsGenericType) continue;
									if (t.GetGenericTypeDefinition().FullName != "UniRx.ReactiveProperty`1") continue;
									var ga = t.GetGenericArguments();
									if (ga == null || ga.Length != 1) continue;
									if (!ReferenceEquals(ga[0], gameDetailType)) continue;
									// property Value setter name: set_Value
									var mi = t.GetMethod("set_Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
									if (mi == null) continue;
									try
									{
										val.Patch((MethodBase)mi, null, new HarmonyMethod(post), null, null, null);
										MelonLogger.Msg("TolkExporter: patched UniRx.ReactiveProperty<GameDetailSo>.set_Value (music select)");
									}
									catch { }
								}
								catch { }
							}
						}
					}
				}
			}
			catch (Exception exMusicSel)
			{
				MelonLogger.Warning("TolkExporter: patch MusicSelect selection exception: " + exMusicSel);
			}

			// 翻頁/Tab 標題：所有可翻頁介面都用 Service.TryToNextTab/TryToPreviousTab 觸發
			try
			{
				PatchTabService(val, "PastelParade.HubGameService");
				PatchTabService(val, "PastelParade.HubNovelService");
				PatchTabService(val, "PastelParade.HubSoundTestService");
				PatchTabService(val, "PastelParade.GalleryService");
			}
			catch (Exception exTabSvc)
			{
				MelonLogger.Warning("TolkExporter: patch tab services exception: " + exTabSvc);
			}

			// 標題/主選單狀態：進入主選單時先觸發一次朗讀（對照 Pastel☆Parade 原始碼：TitleInitializeState.OnStateBegin）
			Type typeTitleInit = AccessTools.TypeByName("PastelParade.TitleInitializeState");
			if (typeTitleInit != null)
			{
				MethodInfo mBegin = typeTitleInit.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("TitleInitializeState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mBegin != null && mPost != null)
				{
					val.Patch((MethodBase)mBegin, (HarmonyMethod)null, new HarmonyMethod(mPost), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.TitleInitializeState.OnStateBegin");
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.TitleInitializeState.OnStateBegin not found or postfix missing");
				}
			}
			else
			{
				MelonLogger.Msg("TolkExporter: PastelParade.TitleInitializeState type not found");
			}
			try
			{
				// NOTE: 不要全域 patch TMP_Text.set_text / UI.Text.set_text。
				// 這會被對話框打字機效果每 frame/每字呼叫，導致語音狂刷。
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("TolkExporter: patch TMP exception: " + ex);
			}
			try
			{
				// 同上：避免全域 set_text patch 造成 UI 文字每 frame 觸發。
			}
			catch (Exception ex2)
			{
				MelonLogger.Warning("TolkExporter: patch UI.Text exception: " + ex2);
			}

			// Timing 調整提示文字：這行是 UI prefab 資產文字，正確時機是 TMP 第一次 OnEnable / 或其父物件 SetActive。
			// Unity/TMP 可能沒有 override TMP_Text.OnEnable（而是繼承自 MaskableGraphic），
			// 因此 patch 基底 MaskableGraphic.OnEnable，再在 postfix 內用型別判斷只處理 TMP。
			try
			{
				Type mgType = Type.GetType("UnityEngine.UI.MaskableGraphic, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.MaskableGraphic");
				if (mgType != null)
				{
					MethodInfo mOnEnable = mgType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("TMPText_OnEnable_TimingHint_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mOnEnable != null && post != null)
					{
						val.Patch((MethodBase)mOnEnable, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.MaskableGraphic.OnEnable (timing hint)");
					}
				}
			}
			catch { }

			// 對話框（TalkPanelCtrl）：在「等待輸入 icon 出現」時才朗讀一次（一句台詞完成）。
			try
			{
				Type typeTalk = AccessTools.TypeByName("PastelParade.TalkPanelCtrl");
				if (typeTalk != null)
				{
					MethodInfo m = typeTalk.GetMethod("ShowWaitInputIcon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("TalkPanelCtrl_ShowWaitInputIcon_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, (HarmonyMethod)null, new HarmonyMethod(mPost), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.TalkPanelCtrl.ShowWaitInputIcon");
					}
					else
					{
						MelonLogger.Msg("TolkExporter: PastelParade.TalkPanelCtrl.ShowWaitInputIcon not found or postfix missing");
					}
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.TalkPanelCtrl type not found");
				}
			}
			catch (Exception exTalk)
			{
				MelonLogger.Warning("TolkExporter: patch TalkPanelCtrl exception: " + exTalk);
			}

			// 教學關卡：剩餘次數（TutorialUI.ShowLeftCount）
			try
			{
				Type typeTut = AccessTools.TypeByName("PastelParade.TutorialUI");
				if (typeTut != null)
				{
					MethodInfo m = typeTut.GetMethod("ShowLeftCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("TutorialUI_ShowLeftCount_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, (HarmonyMethod)null, new HarmonyMethod(mPost), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.TutorialUI.ShowLeftCount");
					}
					else
					{
						MelonLogger.Msg("TolkExporter: PastelParade.TutorialUI.ShowLeftCount not found or postfix missing");
					}
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.TutorialUI type not found");
				}
			}
			catch (Exception exTut)
			{
				MelonLogger.Warning("TolkExporter: patch TutorialUI exception: " + exTut);
			}

			// 設定分類標題：在各設定 state 進入時，用「UI 畫面上真正顯示」的文字推斷並朗讀一次（不硬寫字串）
			PatchStateBeginSpeakCategory(val, "PastelParade.UISoundSettingsState");
			PatchStateBeginSpeakCategory(val, "PastelParade.UIDisplaySettings");
			PatchStateBeginSpeakCategory(val, "PastelParade.UITimingSettingState");
			PatchStateBeginSpeakCategory(val, "PastelParade.UILocalizeState");

			// 設定畫面：同一個 state 內群組/選項被動態重建（Refresh/Rebuild...）時，要重新啟動分類標題朗讀窗口。
			// 依使用者說明：不要掛在 OnStateBegin；應掛在「清空再重建」函數結尾。
			try { PatchSettingsRebuildRequest(val, "PastelParade.UIDisplaySettings"); } catch { }

			// 設定 slider 值：調整時即時朗讀（只在該 slider 目前被選取時）
			try
			{
				Type sliderType = AccessTools.TypeByName("UnityEngine.UI.Slider");
				if (sliderType != null)
				{
					MethodInfo m = sliderType.GetMethod("set_value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("Slider_set_value_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(mPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Slider.set_value");
					}

					// 有些情況會走 SetValueWithoutNotify 或內部 Set()，補上這兩個才能確保「調整就會唸」。
					MethodInfo mNoNotify = sliderType.GetMethod("SetValueWithoutNotify", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mNoNotifyPost = typeof(Patches).GetMethod("Slider_SetValueWithoutNotify_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mNoNotify != null && mNoNotifyPost != null)
					{
						val.Patch((MethodBase)mNoNotify, null, new HarmonyMethod(mNoNotifyPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Slider.SetValueWithoutNotify");
					}

					MethodInfo mSet = sliderType.GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
					MethodInfo mSetPost = typeof(Patches).GetMethod("Slider_Set_Internal_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mSet != null && mSetPost != null)
					{
						val.Patch((MethodBase)mSet, null, new HarmonyMethod(mSetPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Slider.Set (internal)");
					}
				}
			}
			catch (Exception exSlider)
			{
				MelonLogger.Warning("TolkExporter: patch Slider.set_value exception: " + exSlider);
			}

			// 設定 toggle 值：切換時即時朗讀（華感/全螢幕/vsync/anti alias 等都走 Toggle）
			try
			{
				Type toggleType = AccessTools.TypeByName("UnityEngine.UI.Toggle");
				if (toggleType != null)
				{
					MethodInfo m = toggleType.GetMethod("set_isOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("Toggle_set_isOn_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(mPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Toggle.set_isOn");
					}

					MethodInfo mNoNotify = toggleType.GetMethod("SetIsOnWithoutNotify", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mNoNotifyPost = typeof(Patches).GetMethod("Toggle_SetIsOnWithoutNotify_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mNoNotify != null && mNoNotifyPost != null)
					{
						val.Patch((MethodBase)mNoNotify, null, new HarmonyMethod(mNoNotifyPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Toggle.SetIsOnWithoutNotify");
					}

					MethodInfo mSet = toggleType.GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
					MethodInfo mSetPost = typeof(Patches).GetMethod("Toggle_Set_Internal_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mSet != null && mSetPost != null)
					{
						val.Patch((MethodBase)mSet, null, new HarmonyMethod(mSetPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Toggle.Set (internal)");
					}
				}
			}
			catch (Exception exToggle)
			{
				MelonLogger.Warning("TolkExporter: patch Toggle exception: " + exToggle);
			}

			// Pastel☆Parade 設定裡的「全螢幕/抗鋸齒/華感」其實是 MornUGUIButton.AsToggle（來自 MornUGUI.dll），
			// 不一定會走 UnityEngine.UI.Toggle。這裡用反射掃描所有載入組件，找出 MornUGUI 的 Toggle 型別並 patch。
			try
			{
				PatchMornUguitoggles(val);
			}
			catch (Exception exMornToggle)
			{
				MelonLogger.Warning("TolkExporter: patch MornUGUI toggles exception: " + exMornToggle);
			}

			// 教學關卡對話：在「完整字串」進入打字機前朗讀一次（避免每 frame/每字刷新）。
			try
			{
				Type typeTut = AccessTools.TypeByName("PastelParade.TutorialUI");
				if (typeTut != null)
				{
					MethodInfo m = typeTut.GetMethod("PlayTextAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPre = typeof(Patches).GetMethod("TutorialUI_PlayTextAsync_Prefix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPre != null)
					{
						val.Patch((MethodBase)m, new HarmonyMethod(mPre), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.TutorialUI.PlayTextAsync");
					}
					else
					{
						MelonLogger.Msg("TolkExporter: PastelParade.TutorialUI.PlayTextAsync not found or prefix missing");
					}
				}
			}
			catch (Exception exTut2)
			{
				MelonLogger.Warning("TolkExporter: patch TutorialUI.PlayTextAsync exception: " + exTut2);
			}

			// 劇情/小說：依遊戲原始碼 `TutorialUI` 是透過 `MornNovelUtil.DOTextAsync(...)` 驅動打字機效果。
			// 劇情 NovelScene 也會走同一套 util，因此 patch DOTextAsync 的「完整字串入口」最穩定。
			// 注意：這裡只在開始時朗讀一次，避免每字/每 frame 刷新。
			try
			{
				Type novelUtil = AccessTools.TypeByName("MornNovel.MornNovelUtil") ?? AccessTools.TypeByName("MornNovelUtil");
				if (novelUtil != null)
				{
					MethodInfo mPre = typeof(Patches).GetMethod("MornNovelUtil_DOTextAsync_Prefix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mPre != null)
					{
						foreach (var m in novelUtil.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
						{
							if (m == null) continue;
							if (m.Name != "DOTextAsync") continue;
							var ps = m.GetParameters();
							if (ps == null || ps.Length == 0) continue;
							if (ps[0].ParameterType != typeof(string)) continue;
							try
							{
								val.Patch((MethodBase)m, new HarmonyMethod(mPre), null, null, null, null);
							}
							catch { }
						}
						MelonLogger.Msg("TolkExporter: patched MornNovelUtil.DOTextAsync (story)");
					}
				}
			}
			catch (Exception exNovel)
			{
				MelonLogger.Warning("TolkExporter: patch MornNovelUtil.DOTextAsync exception: " + exNovel);
			}

			// 評價/結果畫面：ResultView.SetUp(RuntimePlayData) 會把「畫面上實際顯示」的文字塞進 TMP
			try
			{
				Type typeResultView = AccessTools.TypeByName("PastelParade.Results.ResultView");
				if (typeResultView != null)
				{
					MethodInfo m = typeResultView.GetMethod("SetUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("ResultView_SetUp_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(mPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.Results.ResultView.SetUp");
					}
					else
					{
						MelonLogger.Msg("TolkExporter: PastelParade.Results.ResultView.SetUp not found or postfix missing");
					}
				}
				else
				{
					MelonLogger.Msg("TolkExporter: PastelParade.Results.ResultView type not found");
				}
			}
			catch (Exception exResult)
			{
				MelonLogger.Warning("TolkExporter: patch ResultView.SetUp exception: " + exResult);
			}

			// Result 進場點（依原始碼）：ResultInitState.OnStateBegin() 會呼叫 _view.SetUp(latestResultData)。
			// 多送若發生，應該從這個明確進場點去做最小抑制，而不是在 ResultView.SetUp 內推測原因。
			try
			{
				Type t = AccessTools.TypeByName("PastelParade.ResultInitState");
				if (t != null)
				{
					MethodInfo m = t.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("ResultInitState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && post != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.ResultInitState.OnStateBegin (suppress selection)");
					}
				}
			}
			catch { }

			// Result 期間限定：監聽 TMP_Text/UI.Text.set_text（模仿 patch2 的成功點，但只在結果 UI 範圍內才會真正輸出）
			try
			{
				Type tmpTextType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
				if (tmpTextType != null)
				{
					MethodInfo mSetText = tmpTextType.GetMethod("set_text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("TMPText_set_text_ResultOnly_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mSetText != null && post != null)
					{
						val.Patch((MethodBase)mSetText, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched TMPro.TMP_Text.set_text (result-only)");
					}
				}
			}
			catch { }
			try
			{
				Type uiTextType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
				if (uiTextType != null)
				{
					MethodInfo mSetText = uiTextType.GetMethod("set_text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("UnityUIText_set_text_ResultOnly_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (mSetText != null && post != null)
					{
						val.Patch((MethodBase)mSetText, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched UnityEngine.UI.Text.set_text (result-only)");
					}
				}
			}
			catch { }

			// Result 離場點：ResultCloseState.OnStateBegin
			try
			{
				Type t = AccessTools.TypeByName("PastelParade.ResultCloseState");
				if (t != null)
				{
					MethodInfo m = t.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("ResultCloseState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && post != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.ResultCloseState.OnStateBegin (clear result context)");
					}
				}
			}
			catch { }

			// 關卡內顯示文字（判定差值）：TimingText._diffText.text
			// 依原始碼：`Pastel☆Parade/TimingText.cs`，在 TimingText.Set(BeatEvaluation) 內會呼叫 SetNearDifText 並更新 _diffText.text
			try
			{
				Type typeTimingText = AccessTools.TypeByName("PastelParade.TimingText");
				if (typeTimingText != null)
				{
					MethodInfo m = typeTimingText.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo mPost = typeof(Patches).GetMethod("TimingText_Set_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && mPost != null)
					{
						val.Patch((MethodBase)m, null, new HarmonyMethod(mPost), null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.TimingText.Set");
					}
					else
					{
						MelonLogger.Msg("TolkExporter: PastelParade.TimingText.Set not found or postfix missing");
					}
				}
			}
			catch (Exception exTimingText)
			{
				MelonLogger.Warning("TolkExporter: patch TimingText.Set exception: " + exTimingText);
			}

			// Ending（Parade）字幕/名單：依原始碼 `Pastel☆Parade/ParadeCredit.cs`
			// `ParadeCredit.InitializeAsync` 會在 CreditInDelay 後寫入 `_text.text`（字幕/名單）。
			// 直接 patch 這個方法並用同樣 delay 排程朗讀，避免全域 set_text hook 造成劇情捲動變慢。
			try
			{
				Type t = AccessTools.TypeByName("PastelParade.ParadeCredit");
				if (t != null)
				{
					MethodInfo m = t.GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo pre = typeof(Patches).GetMethod("ParadeCredit_InitializeAsync_Prefix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && pre != null)
					{
						val.Patch((MethodBase)m, new HarmonyMethod(pre), null, null, null, null);
						MelonLogger.Msg("TolkExporter: patched PastelParade.ParadeCredit.InitializeAsync (ending subtitles)");
					}
				}
			}
			catch { }

			// 不要硬寫設定頁標題（要忠實呈現遊戲內容）。
			// 設定頁的朗讀交給 selection + 讀取 UI 真正顯示的 label/value。
			try
			{
				Type type6 = AccessTools.TypeByName("UnityEngine.EventSystems.EventSystem");
				if (type6 != null)
				{
					MethodInfo[] array = (from m in type6.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
						where m.Name == "SetSelectedGameObject"
						select m).ToArray();
					MethodInfo method11 = typeof(Patches).GetMethod("EventSystem_SetSelectedGameObject_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					MethodInfo[] array2 = array;
					foreach (MethodInfo methodInfo in array2)
					{
						try
						{
							val.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(method11), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}

			MelonLogger.Msg("TolkExporter: ApplyPatches() done");
			_fullPatchesApplied = true;
			try
			{
				Main.Instance?.OnPatchesApplied();
			}
			catch
			{
			}
		}
		catch (Exception ex3)
		{
			MelonLogger.Error("TolkExporter: ApplyPatches exception: " + ex3);
		}
	}

	// 只 patch「非常早就會出現」的 UI，避免因為延遲/耗時 patch 流程而漏掉第一次顯示。
	public static void ApplyEarlyPatches()
	{
		if (_earlyPatchesApplied) return;
		try
		{
			var harmony = new HarmonyLib.Harmony("com.assistant.tolkexporter");
			TryPatchEarly(harmony);
			_earlyPatchesApplied = true;
		}
		catch { }
	}

	private static void TryPatchEarly(HarmonyLib.Harmony harmony)
	{
		try
		{
			// 地圖/區域名稱（WorldMapParentUI.Start -> _worldName.text）
			Type typeWorldMapParent = AccessTools.TypeByName("PastelParade.WorldMapParentUI");
			if (!_patchedEarlyWorldMapParentStart && typeWorldMapParent != null)
			{
				MethodInfo mStart = typeWorldMapParent.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("WorldMapParentUI_Start_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mStart != null && mPost != null)
				{
					harmony.Patch((MethodBase)mStart, null, new HarmonyMethod(mPost), null, null, null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.WorldMapParentUI.Start (map name)");
					_patchedEarlyWorldMapParentStart = true;
				}
			}

			// 音樂選單標頭（MusicSelectState.UpdateUI -> _header.text）
			Type typeMusicSelect = AccessTools.TypeByName("PastelParade.MusicSelectState");
			if (!_patchedEarlyMusicSelectUpdateUI && typeMusicSelect != null)
			{
				MethodInfo mUpdate = typeMusicSelect.GetMethod("UpdateUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("MusicSelectState_UpdateUI_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mUpdate != null && mPost != null)
				{
					harmony.Patch((MethodBase)mUpdate, null, new HarmonyMethod(mPost), null, null, null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.MusicSelectState.UpdateUI (header)");
					_patchedEarlyMusicSelectUpdateUI = true;
				}
			}

			// 地圖移動提示（WorldMapAreaMoveUIState.OnStateBegin -> _text.text）
			Type typeMove = AccessTools.TypeByName("PastelParade.WorldMapAreaMoveUIState");
			if (!_patchedEarlyWorldMapAreaMoveBegin && typeMove != null)
			{
				MethodInfo mBegin = typeMove.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("WorldMapAreaMoveUIState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mBegin != null && mPost != null)
				{
					harmony.Patch((MethodBase)mBegin, null, new HarmonyMethod(mPost), null, null, null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.WorldMapAreaMoveUIState.OnStateBegin (area move)");
					_patchedEarlyWorldMapAreaMoveBegin = true;
				}
			}

			// 遊戲版本文字（Title 等場景很早就出現）：GameVersionText.Start -> _text.text
			Type typeGameVersion = AccessTools.TypeByName("PastelParade.GameVersionText");
			if (!_patchedEarlyGameVersionStart && typeGameVersion != null)
			{
				MethodInfo mStart = typeGameVersion.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("GameVersionText_Start_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mStart != null && mPost != null)
				{
					harmony.Patch((MethodBase)mStart, null, new HarmonyMethod(mPost), null, null, null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.GameVersionText.Start (version)");
					_patchedEarlyGameVersionStart = true;
				}
			}

			// 載入關卡畫面（GameTransition）：GameTransitionView.SetUp -> _musicName.text
			Type typeGameTransitionView = AccessTools.TypeByName("PastelParade.GameTransition.GameTransitionView");
			if (!_patchedEarlyGameTransitionViewSetUp && typeGameTransitionView != null)
			{
				MethodInfo mSetUp = typeGameTransitionView.GetMethod("SetUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo mPost = typeof(Patches).GetMethod("GameTransitionView_SetUp_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				if (mSetUp != null && mPost != null)
				{
					harmony.Patch((MethodBase)mSetUp, null, new HarmonyMethod(mPost), null, null, null);
					MelonLogger.Msg("TolkExporter: patched PastelParade.GameTransition.GameTransitionView.SetUp (loading music name)");
					_patchedEarlyGameTransitionViewSetUp = true;
				}
			}

			// 啟動畫面（Title UI Prefab 的遊戲名稱）：某些版本在 TitleState / UITitleState 內第一次設定 _titleText.text
			// 這裡直接 hook 該 state 的生命週期方法，讀取「最終顯示文字」並送出一次（不走 selection）。
			if (!_patchedEarlyTitleStateTitleText)
			{
				Type tTitle = AccessTools.TypeByName("PastelParade.TitleState") ?? AccessTools.TypeByName("PastelParade.UITitleState");
				if (tTitle != null)
				{
					MethodInfo post = typeof(Patches).GetMethod("TitleState_Lifecycle_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (post != null)
					{
						string[] names = { "OnStateBegin", "Show", "Start" };
						for (int i = 0; i < names.Length; i++)
						{
							try
							{
								// patch all overloads with the same name
								var ms = tTitle.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								for (int k = 0; k < ms.Length; k++)
								{
									var m = ms[k];
									if (m == null) continue;
									if (!string.Equals(m.Name, names[i], StringComparison.Ordinal)) continue;
									harmony.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
								}
							}
							catch { }
						}
						_patchedEarlyTitleStateTitleText = true;
						MelonLogger.Msg("TolkExporter: patched PastelParade.TitleState/UITitleState lifecycle (title text)");
					}
				}
			}

			// 關卡載入提示（使用者指出來源：UILoadingState.cs / PastelParade.UILoadingState）
			// 依需求：必須 patch「實際 set_text 的函數」（例如 SetTip/UpdateTip/Show），而不是靠 state/selection/Update。
			if (!_patchedEarlyUiLoadingStateText)
			{
				Type tLoading = AccessTools.TypeByName("PastelParade.UILoadingState");
				if (tLoading != null)
				{
					MethodInfo postLife = typeof(Patches).GetMethod("UILoadingState_Lifecycle_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					MethodInfo postSetTip = typeof(Patches).GetMethod("UILoadingState_SetTip_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					MethodInfo postUpdateTip = typeof(Patches).GetMethod("UILoadingState_UpdateTipLike_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (postLife != null)
					{
						// lifecycle: only for capture window (no speaking)
						string[] lifeNames = { "OnStateBegin", "Show", "Start", "SetUp" };
						var ms = tLoading.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						for (int i = 0; i < lifeNames.Length; i++)
							for (int k = 0; k < ms.Length; k++)
							{
								var m = ms[k];
								if (m == null) continue;
								if (!string.Equals(m.Name, lifeNames[i], StringComparison.Ordinal)) continue;
								try { harmony.Patch((MethodBase)m, null, new HarmonyMethod(postLife), null, null, null); } catch { }
							}

						// Patch tip setters (speak once)
						if (postSetTip != null || postUpdateTip != null)
							PatchUILoadingStateTipSetters(harmony, tLoading, postSetTip, postUpdateTip);

						_patchedEarlyUiLoadingStateText = true;
						MelonLogger.Msg("TolkExporter: patched PastelParade.UILoadingState lifecycle (loading tips)");
					}
				}
			}

			// 本版：啟動畫面/載入提示文字來源在 DebugPanel.AddText 的共用文字流中。
			// 依需求：DebugPanel.AddText 只做 enqueue，不直接朗讀；真正朗讀由 exporter 的 DebugFlowPhase 決定。
			if (!_patchedEarlyDebugPanelAddText)
			{
				Type tDebugPanel = AccessTools.TypeByName("PastelParade.Common.DebugPanel");
				if (tDebugPanel != null)
				{
					MethodInfo m = tDebugPanel.GetMethod("AddText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					MethodInfo post = typeof(Patches).GetMethod("DebugPanel_AddText_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (m != null && post != null)
					{
						harmony.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
						_patchedEarlyDebugPanelAddText = true;
						MelonLogger.Msg("TolkExporter: patched PastelParade.Common.DebugPanel.AddText (enqueue only)");
					}
				}
			}

		}
		catch { }
	}

	private static void DebugPanel_AddText_Postfix(string text)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (string.IsNullOrWhiteSpace(text)) return;
			// enqueue only; do not speak here
			Main.Instance?.EnqueueDebugFlowText(text);
		}
		catch { }
	}

	private static void PatchUILoadingStateTipSetters(HarmonyLib.Harmony harmony, Type tLoading, MethodInfo postSetTip, MethodInfo postUpdateTip)
	{
		try
		{
			if (harmony == null || tLoading == null) return;
			var ms = tLoading.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (ms == null || ms.Length == 0) return;

			int patched = 0;
			for (int i = 0; i < ms.Length; i++)
			{
				var m = ms[i];
				if (m == null) continue;

				// Prefer SetTip(string text)
				if (postSetTip != null && string.Equals(m.Name, "SetTip", StringComparison.Ordinal))
				{
					try
					{
						var ps = m.GetParameters();
						if (ps != null && ps.Length == 1 && ps[0].ParameterType == typeof(string))
						{
							harmony.Patch((MethodBase)m, null, new HarmonyMethod(postSetTip), null, null, null);
							patched++;
							continue;
						}
					}
					catch { }
				}

				// UpdateTip / Show / OnStateBegin: read _tipText.text / _messageText.text in postfix
				if (postUpdateTip != null)
				{
					if (string.Equals(m.Name, "UpdateTip", StringComparison.Ordinal) ||
					    string.Equals(m.Name, "Show", StringComparison.Ordinal) ||
					    string.Equals(m.Name, "OnStateBegin", StringComparison.Ordinal))
					{
						try
						{
							harmony.Patch((MethodBase)m, null, new HarmonyMethod(postUpdateTip), null, null, null);
							patched++;
						}
						catch { }
					}
				}
			}

			if (patched > 0)
				MelonLogger.Msg("TolkExporter: patched UILoadingState tip setters. count=" + patched);
		}
		catch { }
	}

	private static void UILoadingState_Lifecycle_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// 僅開啟 capture window：把 Prefab 靜態 TMP id 註冊到 _loadingTmpTextIds（不在這裡朗讀）
			try { BeginLoadingPrefabCapture(__instance); } catch { }
		}
		catch { }
	}

	private static void UILoadingState_SetTip_Postfix(object __instance, string text)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;
			text = StripTmpRichText(text ?? "").Trim();
			if (string.IsNullOrWhiteSpace(text)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastUiLoadingBundle, text, StringComparison.Ordinal) && (now - _lastUiLoadingBundleAt).TotalMilliseconds < 1500)
				return;
			_lastUiLoadingBundle = text;
			_lastUiLoadingBundleAt = now;
			Main.Instance?.SendToTolk(text);
		}
		catch { }
	}

	private static void UILoadingState_UpdateTipLike_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// Try fields that commonly hold TMP objects
			string text = null;
			try
			{
				object tipTmp = __instance.GetType().GetField("_tipText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				text = tipTmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tipTmp) as string;
			}
			catch { text = null; }
			if (string.IsNullOrWhiteSpace(text))
			{
				try
				{
					object msgTmp = __instance.GetType().GetField("_messageText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
					text = msgTmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(msgTmp) as string;
				}
				catch { text = null; }
			}

			// Fallback: pick best visible non-selectable text under this component
			if (string.IsNullOrWhiteSpace(text))
			{
				try
				{
					foreach (var s in TryCollectVisibleTextsUnderComponent(__instance, maxCount: 2))
					{
						if (string.IsNullOrWhiteSpace(s)) continue;
						text = s;
						break;
					}
				}
				catch { text = null; }
			}

			text = StripTmpRichText(text ?? "").Trim();
			if (string.IsNullOrWhiteSpace(text)) return;
			if (text.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastUiLoadingBundle, text, StringComparison.Ordinal) && (now - _lastUiLoadingBundleAt).TotalMilliseconds < 1500)
				return;
			_lastUiLoadingBundle = text;
			_lastUiLoadingBundleAt = now;
			Main.Instance?.SendToTolk(text);
		}
		catch { }
	}

	private static void TitleState_Lifecycle_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// Prefer known field name
			string s = null;
			try
			{
				object tmp = __instance.GetType().GetField("_titleText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			}
			catch { s = null; }

			// Fallback: pick best visible, non-selectable text under this component
			if (string.IsNullOrWhiteSpace(s))
			{
				try
				{
					foreach (var t in TryCollectVisibleTextsUnderComponent(__instance, maxCount: 2))
					{
						if (string.IsNullOrWhiteSpace(t)) continue;
						s = t;
						break;
					}
				}
				catch { s = null; }
			}

			s = StripTmpRichText(s ?? "").Trim();
			if (string.IsNullOrWhiteSpace(s)) return;
			if (s.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastTitleStateTitleText, s, StringComparison.Ordinal) && (now - _lastTitleStateTitleAt).TotalMilliseconds < 2000)
				return;
			_lastTitleStateTitleText = s;
			_lastTitleStateTitleAt = now;
			Main.Instance?.SendToTolk(s);
		}
		catch { }
	}

	private static void GameVersionText_Start_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			object tmp = __instance.GetType().GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			if (tmp == null) return;
			string s = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastGameVersionText, s, StringComparison.Ordinal) && (now - _lastGameVersionAt).TotalMilliseconds < 1000)
				return;
			_lastGameVersionText = s;
			_lastGameVersionAt = now;

			Main.Instance?.SendToTolk(s);
		}
		catch { }
	}

	private static void GameTransitionView_SetUp_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// 依 cursor_fix：補接「Loading 顯示開始點」，並且每次被設定時只唸新內容。
			// GameTransitionView.SetUp 是顯示/更新載入 Overlay 的明確進入點。
			BeginLoadingPrefabCapture(__instance);

			// 1) 先抓「音樂名」(原本行為)
			string music = null;
			try
			{
				object tmp = __instance.GetType().GetField("_musicName", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				music = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
				music = StripTmpRichText(music).Trim();
				if (string.IsNullOrWhiteSpace(music)) music = null;
			}
			catch { music = null; }

			// 2) 再從「畫面上真正顯示」的文字補齊 Loading / 提示（很多是靜態 TMP，不一定由原始碼 set_text）
			var extra = new List<string>();
			try
			{
				foreach (var s in TryCollectVisibleTextsUnderComponent(__instance, maxCount: 4))
				{
					if (string.IsNullOrWhiteSpace(s)) continue;
					if (music != null && string.Equals(s, music, StringComparison.Ordinal)) continue;
					extra.Add(s);
				}
			}
			catch { }

			// 組合輸出（最多 2 段，避免把整個 overlay 都唸一遍）
			var parts = new List<string>();
			// Loading/提示優先（extra 依 font/位置挑過）
			for (int i = 0; i < extra.Count && parts.Count < 2; i++)
				parts.Add(extra[i]);
			if (parts.Count < 2 && !string.IsNullOrWhiteSpace(music))
				parts.Add(music);

			if (parts.Count == 0) return;
			string spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
			if (string.IsNullOrWhiteSpace(spoken)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastGameTransitionBundle, spoken, StringComparison.Ordinal) && (now - _lastGameTransitionBundleAt).TotalMilliseconds < 600)
				return;
			_lastGameTransitionBundle = spoken;
			_lastGameTransitionBundleAt = now;

			// 仍保留 music 的最小去重記錄（避免其他地方只拿 music 作比較）
			if (!string.IsNullOrWhiteSpace(music))
			{
				_lastGameTransitionMusic = music;
				_lastGameTransitionMusicAt = now;
			}

			Main.Instance?.SendToTolk(spoken);
		}
		catch { }
	}

	// bump feature removed

	// 從某個 view/component 的 GameObject 下，抓「目前可見」的文字（TMP + UI.Text），挑最像標題/提示的前幾個。
	// 目的：補齊 Loading/啟動畫面中「靜態文字」或「同一物件多次更新」造成的漏唸。
	private static IEnumerable<string> TryCollectVisibleTextsUnderComponent(object anyComponent, int maxCount)
	{
		if (anyComponent == null) yield break;
		if (maxCount <= 0) yield break;

		object rootGo = null;
		try
		{
			rootGo = anyComponent.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(anyComponent);
		}
		catch { rootGo = null; }
		if (rootGo == null) yield break;

		// 取 TMP_Text + UI.Text
		Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
		Type uiTextType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Text");
		if (tmpType == null && uiTextType == null) yield break;

		Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Selectable");

		var candidates = new List<(string s, double score)>();

		void AddFromArray(Array arr, bool isTmp)
		{
			if (arr == null) return;
			for (int i = 0; i < arr.Length; i++)
			{
				var c = arr.GetValue(i);
				if (c == null) continue;
				if (!IsComponentActive(c)) continue;

				var s = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c) as string;
				s = StripTmpRichText(s).Trim();
				if (string.IsNullOrWhiteSpace(s)) continue;

				// 避免把純數字/值當提示
				if (s.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) continue;
				double _;
				if (double.TryParse(s, out _)) continue;
				if (s.Length > 64) continue;

				object go = null;
				try { go = c.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c); }
				catch { go = null; }
				if (go == null) continue;

				// 避免按鈕文字（Loading overlay 的提示通常不是 selectable）
				if (selectableType != null)
				{
					try
					{
						var hasSel = FindComponentInParents(go, selectableType, 12);
						if (hasSel != null) continue;
					}
					catch { }
				}

				float fontSize = 0f;
				try
				{
					if (isTmp)
					{
						var fs = c.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
						if (fs != null) fontSize = Convert.ToSingle(fs);
					}
				}
				catch { }

				float y = 0f;
				try
				{
					var rt = c.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					var pos = rt?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rt);
					if (pos != null)
					{
						var fy = pos.GetType().GetField("y")?.GetValue(pos);
						if (fy != null) y = Convert.ToSingle(fy);
					}
				}
				catch { }

				double score = fontSize * 1000.0 + y;
				candidates.Add((s, score));
			}
		}

		try
		{
			// GameObject.GetComponentsInChildren(Type, bool)
			if (tmpType != null)
			{
				var mi = rootGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
				var arr = mi?.Invoke(rootGo, new object[] { tmpType, true }) as Array;
				AddFromArray(arr, isTmp: true);
			}
			if (uiTextType != null)
			{
				var mi = rootGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
				var arr = mi?.Invoke(rootGo, new object[] { uiTextType, true }) as Array;
				AddFromArray(arr, isTmp: false);
			}
		}
		catch { }

		// 依 score 排序，去重後取前 maxCount
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var it in candidates.OrderByDescending(x => x.score))
		{
			if (seen.Contains(it.s)) continue;
			seen.Add(it.s);
			yield return it.s;
			if (seen.Count >= maxCount) yield break;
		}
	}

	private static bool IsDelegateTargetTalkPanelCtrl(object callback)
	{
		try
		{
			var del = callback as Delegate;
			var target = del?.Target;
			if (target == null) return false;
			var tn = target.GetType().FullName ?? "";
			// 依遊戲原始碼：TutorialUI 會傳入 _talkPanel.SetText / _talkPanel.ShowWaitInputIcon
			// 這類型的 DOTextAsync 應該交由 TalkPanelCtrl.ShowWaitInputIcon（或 TutorialUI.PlayTextAsync）處理，不要在 DOTextAsync 入口朗讀。
			bool isTalkPanel = tn.IndexOf("PastelParade.TalkPanelCtrl", StringComparison.Ordinal) >= 0 ||
			                   tn.EndsWith(".TalkPanelCtrl", StringComparison.Ordinal);
			if (!isTalkPanel) return false;

			// 只針對「教學用的 talk panel」分流；避免誤傷其他也用 TalkPanelCtrl 的劇情/系統對話
			return IsTutorialTalkPanel(target);
		}
		catch { return false; }
	}

	private static bool IsTutorialTalkPanel(object talkPanelCtrl)
	{
		try
		{
			if (talkPanelCtrl == null) return false;
			int id = GetUnityInstanceId(talkPanelCtrl);
			if (id == 0) return false;

			// 快取避免每次 ShowWaitInputIcon 都掃場景
			var now = DateTime.Now;
			if ((now - _tutorialTalkPanelIdsRefreshedAt).TotalMilliseconds > 1000 || _tutorialTalkPanelIds.Count == 0)
			{
				RefreshTutorialTalkPanelIds();
			}

			lock (_tutorialTalkPanelIds)
			{
				return _tutorialTalkPanelIds.Contains(id);
			}
		}
		catch { }
		return false;
	}

	private static object FindComponentInParents(object go, Type componentType, int maxDepth)
	{
		try
		{
			if (go == null || componentType == null) return null;
			object curGo = go;
			for (int i = 0; i < maxDepth; i++)
			{
				var comp = TryGetComponent(curGo, componentType);
				if (comp != null) return comp;
				var tr = curGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(curGo);
				var parentTr = tr?.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				curGo = parentTr?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parentTr);
				if (curGo == null) break;
			}
		}
		catch { }
		return null;
	}

	private static object TryGetComponent(object gameObject, Type componentType)
	{
		try
		{
			if (gameObject == null || componentType == null) return null;
			var mi = gameObject.GetType().GetMethod("GetComponent", new[] { typeof(Type) });
			return mi?.Invoke(gameObject, new object[] { componentType });
		}
		catch { return null; }
	}

	private static object GetCurrentSelectedGameObject()
	{
		try
		{
			Type esType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI")
			             ?? Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.CoreModule")
			             ?? Type.GetType("UnityEngine.EventSystems.EventSystem");
			var es = esType?.GetProperty("current", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			return esType?.GetProperty("currentSelectedGameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(es);
		}
		catch { return null; }
	}

	private static void HubService_SetPreview_Postfix(object __instance, object background, string text)
	{
		try
		{
			if (!AutoSpeakEnabled) return;

			var selGo = GetCurrentSelectedGameObject();
			if (selGo == null) return;

			// 1) 歌曲視聽：UIHubSoundTestRow.OnFocus -> SetPreview(thumbnail, artistName)
			// 依原始碼：UIHubSoundTest.cs
			try
			{
				Type stRowType = AccessTools.TypeByName("PastelParade.UIHubSoundTestRow");
				if (stRowType != null)
				{
					var stRow = FindComponentInParents(selGo, stRowType, 6);
					if (stRow != null)
					{
						string stMusic = null;
						try
						{
							if (stRow.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(stRow) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { stMusic = s; break; }
								}
							}
						}
						catch { stMusic = null; }

						string artist = StripTmpRichText(text ?? "").Trim();
						var stParts = new List<string>();
						if (!string.IsNullOrWhiteSpace(stMusic)) stParts.Add(stMusic);
						if (!string.IsNullOrWhiteSpace(artist)) stParts.Add(artist);
						var stSpoken = string.Join(" ", stParts.Where(x => !string.IsNullOrWhiteSpace(x)));
						if (!string.IsNullOrWhiteSpace(stSpoken))
						{
							// 翻頁合併輸出：先把焦點內容存起來，交給合併邏輯一次唸
							if (Main.Instance?.IsTabMergePending == true)
								Main.Instance?.SetPendingTabFocusText(stSpoken);
							else
								Main.Instance?.SendToTolk(stSpoken);
						}
						return;
					}
				}
			}
			catch { }

			// 2) Hub 遊戲清單：UIHubGameRow focus 時會傳入 text=""（原始碼）
			if (text != "") return;

			// 2.1) Hub 小說清單：UIHubNovelRow focus 也會傳入 text=""（原始碼：UIHubNovel.UpdateUI）
			try
			{
				Type novelRowType = AccessTools.TypeByName("PastelParade.UIHubNovelRow");
				if (novelRowType != null)
				{
					var nRow = FindComponentInParents(selGo, novelRowType, 6);
					if (nRow != null)
					{
						string nTitle = null;
						try
						{
							if (nRow.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(nRow) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { nTitle = s; break; }
								}
							}
						}
						catch { nTitle = null; }

						if (!string.IsNullOrWhiteSpace(nTitle))
						{
							if (Main.Instance?.IsTabMergePending == true)
								Main.Instance?.SetPendingTabFocusText(nTitle);
							else
								Main.Instance?.SendToTolk(nTitle);
						}
						return;
					}
				}
			}
			catch { }

			Type rowType = AccessTools.TypeByName("PastelParade.UIHubGameRow");
			if (rowType == null) return;
			var row = FindComponentInParents(selGo, rowType, 6);
			if (row == null) return;

			// 直接讀 UI 上顯示的文字（忠實呈現）
			string music = null;
			try
			{
				if (row.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
				{
					for (int i = 0; i < arr.Length; i++)
					{
						var tmp = arr.GetValue(i);
						var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
						s = StripTmpRichText(s).Trim();
						if (!string.IsNullOrWhiteSpace(s)) { music = s; break; }
					}
				}
			}
			catch { music = null; }

			string percent = null;
			try
			{
				if (row.GetType().GetField("_percentTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
				{
					for (int i = 0; i < arr.Length; i++)
					{
						var tmp = arr.GetValue(i);
						var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
						s = StripTmpRichText(s).Trim();
						if (!string.IsNullOrWhiteSpace(s)) { percent = s; break; }
					}
				}
			}
			catch { percent = null; }

			string diff = null;
			try
			{
				var tmp = row.GetType().GetField("_difficultyText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row);
				var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
				diff = StripTmpRichText(s).Trim();
			}
			catch { diff = null; }

			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(music)) parts.Add(music);
			if (!string.IsNullOrWhiteSpace(percent)) parts.Add(percent);
			if (!string.IsNullOrWhiteSpace(diff)) parts.Add(diff);
			var spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
			if (string.IsNullOrWhiteSpace(spoken)) return;
			if (Main.Instance?.IsTabMergePending == true)
				Main.Instance?.SetPendingTabFocusText(spoken);
			else
				Main.Instance?.SendToTolk(spoken);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: HubService_SetPreview_Postfix failed: " + ex);
		}
	}

	private static void MusicSelectService_get_DetailRp_Postfix(object __instance, object __result)
	{
		try
		{
			if (__result == null) return;
			// 記住目前這個 reactive property instance，讓 set_Value 的 patch 可以精準過濾
			_musicSelectDetailRpInstance = __result;
		}
		catch { }
	}

	private static void ReactivePropertyGameDetail_set_Value_Postfix(object __instance, object __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (_musicSelectDetailRpInstance == null) return;
			if (!ReferenceEquals(__instance, _musicSelectDetailRpInstance)) return;
			if (__0 == null) return;

			// 直接讀當前選取列的 UI 文字（忠實呈現）
			var selGo = GetCurrentSelectedGameObject();
			if (selGo == null) return;
			Type rowType = AccessTools.TypeByName("PastelParade.MusicSelectDetailRowMono");
			if (rowType == null) return;
			var row = FindComponentInParents(selGo, rowType, 6);
			if (row == null) return;

			string title = null;
			try
			{
				if (row.GetType().GetField("_titles", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
				{
					for (int i = 0; i < arr.Length; i++)
					{
						var tmp = arr.GetValue(i);
						var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
						s = StripTmpRichText(s).Trim();
						if (!string.IsNullOrWhiteSpace(s)) { title = s; break; }
					}
				}
			}
			catch { title = null; }

			string percent = null;
			try
			{
				if (row.GetType().GetField("_percentTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
				{
					for (int i = 0; i < arr.Length; i++)
					{
						var tmp = arr.GetValue(i);
						var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
						s = StripTmpRichText(s).Trim();
						if (!string.IsNullOrWhiteSpace(s)) { percent = s; break; }
					}
				}
			}
			catch { percent = null; }

			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
			if (!string.IsNullOrWhiteSpace(percent)) parts.Add(percent);
			var spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
			if (string.IsNullOrWhiteSpace(spoken)) return;
			if (Main.Instance?.IsTabMergePending == true)
				Main.Instance?.SetPendingTabFocusText(spoken);
			else
				Main.Instance?.SendToTolk(spoken);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: ReactivePropertyGameDetail_set_Value_Postfix failed: " + ex);
		}
	}

	private static void PatchTabService(HarmonyLib.Harmony harmony, string typeName)
	{
		try
		{
			Type t = AccessTools.TypeByName(typeName);
			if (t == null) return;
			MethodInfo mNext = t.GetMethod("TryToNextTab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo mPrev = t.GetMethod("TryToPreviousTab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo postNext = typeof(Patches).GetMethod("TabService_TryToNextTab_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo postPrev = typeof(Patches).GetMethod("TabService_TryToPreviousTab_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (mNext != null && postNext != null) harmony.Patch((MethodBase)mNext, null, new HarmonyMethod(postNext), null, null, null);
			if (mPrev != null && postPrev != null) harmony.Patch((MethodBase)mPrev, null, new HarmonyMethod(postPrev), null, null, null);
			MelonLogger.Msg("TolkExporter: patched " + typeName + ".TryToNextTab/TryToPreviousTab (tab title)");
		}
		catch { }
	}

	private static void PatchUpdateUiPostfix(HarmonyLib.Harmony harmony, string typeName, string postfixName)
	{
		try
		{
			Type t = AccessTools.TypeByName(typeName);
			if (t == null) return;
			MethodInfo m = t.GetMethod("UpdateUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo post = typeof(Patches).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic);
			if (m == null || post == null) return;
			harmony.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
			MelonLogger.Msg("TolkExporter: patched " + typeName + ".UpdateUI (re-attach focus speak)");
		}
		catch { }
	}

	private static void UIHubGame_UpdateUI_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.TriggerReadSelection();
		}
		catch { }
	}

	private static void UIHubNovel_UpdateUI_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.TriggerReadSelection();
		}
		catch { }
	}

	private static void UIHubSoundTest_UpdateUI_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.TriggerReadSelection();
		}
		catch { }
	}

	private static void UIGallery_UpdateUI_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.TriggerReadSelection();
		}
		catch { }
	}

	private static void TabService_TryToNextTab_Postfix(object __instance, bool __result)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (!__result) return;
			var s = GetTabTitle(__instance);
			if (string.IsNullOrWhiteSpace(s)) return;
			Main.Instance?.RequestSpeakTabTitleMerged(s);
		}
		catch { }
	}

	private static void TabService_TryToPreviousTab_Postfix(object __instance, bool __result)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (!__result) return;
			var s = GetTabTitle(__instance);
			if (string.IsNullOrWhiteSpace(s)) return;
			Main.Instance?.RequestSpeakTabTitleMerged(s);
		}
		catch { }
	}

	private static string GetTabTitle(object service)
	{
		try
		{
			if (service == null) return null;
			var mi = service.GetType().GetMethod("GetCurrentTabName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			var s = mi?.Invoke(service, null) as string;
			s = StripTmpRichText(s ?? "").Trim();
			return string.IsNullOrWhiteSpace(s) ? null : s;
		}
		catch { return null; }
	}

	internal static string TryBuildFocusTextFromSelection()
	{
		try
		{
			var selGo = GetCurrentSelectedGameObject();
			if (selGo == null) return null;

			// HubGameRow
			try
			{
				Type rowType = AccessTools.TypeByName("PastelParade.UIHubGameRow");
				if (rowType != null)
				{
					var row = FindComponentInParents(selGo, rowType, 6);
					if (row != null)
					{
						string music = null;
						string percent = null;
						string diff = null;
						try
						{
							if (row.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { music = s; break; }
								}
							}
						}
						catch { music = null; }
						try
						{
							if (row.GetType().GetField("_percentTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { percent = s; break; }
								}
							}
						}
						catch { percent = null; }
						try
						{
							var tmp = row.GetType().GetField("_difficultyText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row);
							var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
							diff = StripTmpRichText(s).Trim();
						}
						catch { diff = null; }
						var parts = new List<string>();
						if (!string.IsNullOrWhiteSpace(music)) parts.Add(music);
						if (!string.IsNullOrWhiteSpace(percent)) parts.Add(percent);
						if (!string.IsNullOrWhiteSpace(diff)) parts.Add(diff);
						var spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
						return string.IsNullOrWhiteSpace(spoken) ? null : spoken;
					}
				}
			}
			catch { }

			// HubSoundTestRow
			try
			{
				Type stRowType = AccessTools.TypeByName("PastelParade.UIHubSoundTestRow");
				if (stRowType != null)
				{
					var stRow = FindComponentInParents(selGo, stRowType, 6);
					if (stRow != null)
					{
						string stMusic = null;
						try
						{
							if (stRow.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(stRow) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { stMusic = s; break; }
								}
							}
						}
						catch { stMusic = null; }
						return string.IsNullOrWhiteSpace(stMusic) ? null : stMusic;
					}
				}
			}
			catch { }

			// MusicSelectDetailRowMono
			try
			{
				Type msRowType = AccessTools.TypeByName("PastelParade.MusicSelectDetailRowMono");
				if (msRowType != null)
				{
					var row = FindComponentInParents(selGo, msRowType, 6);
					if (row != null)
					{
						string title = null;
						string percent = null;
						try
						{
							if (row.GetType().GetField("_titles", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { title = s; break; }
								}
							}
						}
						catch { title = null; }
						try
						{
							if (row.GetType().GetField("_percentTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { percent = s; break; }
								}
							}
						}
						catch { percent = null; }
						var parts = new List<string>();
						if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
						if (!string.IsNullOrWhiteSpace(percent)) parts.Add(percent);
						var spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
						return string.IsNullOrWhiteSpace(spoken) ? null : spoken;
					}
				}
			}
			catch { }

			// GalleryRow
			try
			{
				Type gRowType = AccessTools.TypeByName("PastelParade.UIGalleryRow");
				if (gRowType != null)
				{
					var row = FindComponentInParents(selGo, gRowType, 6);
					if (row != null)
					{
						string title = null;
						string date = null;
						try
						{
							if (row.GetType().GetField("_titles", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { title = s; break; }
								}
							}
						}
						catch { title = null; }
						try
						{
							var tmp = row.GetType().GetField("_date", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row);
							var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
							date = StripTmpRichText(s).Trim();
						}
						catch { date = null; }
						var parts = new List<string>();
						if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
						if (!string.IsNullOrWhiteSpace(date)) parts.Add(date);
						var spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
						return string.IsNullOrWhiteSpace(spoken) ? null : spoken;
					}
				}
			}
			catch { }

			// HubNovelRow（fallback）
			try
			{
				Type nRowType = AccessTools.TypeByName("PastelParade.UIHubNovelRow");
				if (nRowType != null)
				{
					var row = FindComponentInParents(selGo, nRowType, 6);
					if (row != null)
					{
						string title = null;
						try
						{
							if (row.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(row) is Array arr)
							{
								for (int i = 0; i < arr.Length; i++)
								{
									var tmp = arr.GetValue(i);
									var s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
									s = StripTmpRichText(s).Trim();
									if (!string.IsNullOrWhiteSpace(s)) { title = s; break; }
								}
							}
						}
						catch { title = null; }
						return string.IsNullOrWhiteSpace(title) ? null : title;
					}
				}
			}
			catch { }
		}
		catch { }
		return null;
	}

	private static void RefreshTutorialTalkPanelIds()
	{
		try
		{
			Type tutType = AccessTools.TypeByName("PastelParade.TutorialUI");
			if (tutType == null) return;
			Array arr = FindUnityObjectsByType(tutType);
			if (arr == null) return;

			var ids = new List<int>();
			for (int i = 0; i < arr.Length; i++)
			{
				var tut = arr.GetValue(i);
				if (tut == null) continue;
				var f = tut.GetType().GetField("_talkPanel", BindingFlags.Instance | BindingFlags.NonPublic);
				var tp = f?.GetValue(tut);
				int id = GetUnityInstanceId(tp);
				if (id != 0) ids.Add(id);
			}

			lock (_tutorialTalkPanelIds)
			{
				_tutorialTalkPanelIds.Clear();
				for (int i = 0; i < ids.Count; i++) _tutorialTalkPanelIds.Add(ids[i]);
			}
			_tutorialTalkPanelIdsRefreshedAt = DateTime.Now;
		}
		catch { }
	}

	private static Array FindUnityObjectsByType(Type t)
	{
		try
		{
			if (t == null) return null;
			Type objType = Type.GetType("UnityEngine.Object, UnityEngine.CoreModule") ?? Type.GetType("UnityEngine.Object");
			if (objType == null) return null;

			// Unity 6: FindObjectsByType(Type, FindObjectsInactive, FindObjectsSortMode)
			var mFindByType = objType.GetMethod("FindObjectsByType", BindingFlags.Public | BindingFlags.Static, null,
				new[] { typeof(Type), Type.GetType("UnityEngine.FindObjectsInactive, UnityEngine.CoreModule") ?? typeof(int), Type.GetType("UnityEngine.FindObjectsSortMode, UnityEngine.CoreModule") ?? typeof(int) }, null);
			if (mFindByType != null)
			{
				// Include inactive, no sorting (1/0)
				return mFindByType.Invoke(null, new object[] { t, 1, 0 }) as Array;
			}

			// Older: FindObjectsOfType(Type, bool)
			var mFind = objType.GetMethod("FindObjectsOfType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type), typeof(bool) }, null);
			if (mFind != null)
			{
				return mFind.Invoke(null, new object[] { t, true }) as Array;
			}

			// Fallback: FindObjectsOfType(Type)
			var mFind2 = objType.GetMethod("FindObjectsOfType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
			return mFind2?.Invoke(null, new object[] { t }) as Array;
		}
		catch { return null; }
	}

	private static int GetUnityInstanceId(object unityObj)
	{
		try
		{
			if (unityObj == null) return 0;
			var m = unityObj.GetType().GetMethod("GetInstanceID", BindingFlags.Instance | BindingFlags.Public);
			if (m == null) return 0;
			return (int)m.Invoke(unityObj, null);
		}
		catch { return 0; }
	}

	private static void PatchStateBeginSpeakCategory(HarmonyLib.Harmony harmony, string typeName)
	{
		try
		{
			Type t = AccessTools.TypeByName(typeName);
			if (t == null) return;
			MethodInfo m = t.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo post = typeof(Patches).GetMethod("SettingsCategoryState_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (m == null || post == null) return;
			harmony.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
			MelonLogger.Msg("TolkExporter: patched " + typeName + ".OnStateBegin (category title)");

			// 用 OnStateEnd 清掉「目前在設定頁」旗標，避免離開後主選單也被當成設定頁，造成 0/1 汙染。
			MethodInfo mEnd = t.GetMethod("OnStateEnd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo postEnd = typeof(Patches).GetMethod("SettingsCategoryState_OnStateEnd_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (mEnd != null && postEnd != null)
			{
				harmony.Patch((MethodBase)mEnd, null, new HarmonyMethod(postEnd), null, null, null);
				MelonLogger.Msg("TolkExporter: patched " + typeName + ".OnStateEnd (settings context)");
			}
		}
		catch { }
	}

	private static void PatchSettingsRebuildRequest(HarmonyLib.Harmony harmony, string typeName)
	{
		try
		{
			Type t = AccessTools.TypeByName(typeName);
			if (t == null) return;
			MethodInfo post = typeof(Patches).GetMethod("SettingsRebuild_RequestCategoryTitle_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (post == null) return;

			// Known patterns (actual name differs by build)
			string[] names = { "Refresh", "RefreshUI", "Rebuild", "BuildOptions", "ApplySettings" };
			var ms = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (ms == null || ms.Length == 0) return;

			int patched = 0;
			for (int i = 0; i < ms.Length; i++)
			{
				var m = ms[i];
				if (m == null) continue;
				for (int j = 0; j < names.Length; j++)
				{
					if (!string.Equals(m.Name, names[j], StringComparison.Ordinal)) continue;
					try
					{
						harmony.Patch((MethodBase)m, null, new HarmonyMethod(post), null, null, null);
						patched++;
					}
					catch { }
					break;
				}
			}
			if (patched > 0)
				MelonLogger.Msg("TolkExporter: patched " + typeName + " rebuild-like methods (category title re-request). count=" + patched);
		}
		catch { }
	}

	private static void SettingsRebuild_RequestCategoryTitle_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;
			// 每次「重建完成點」都重新啟動 pending window，讓後續 title set_text 可以被捕捉
			Main.Instance?.RequestSpeakSettingsCategoryTitle(__instance, fromRebuild: true);
		}
		catch { }
	}

	private static void SettingsCategoryState_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// 依遊戲原始碼，在進入設定頁時把「真正的 toggle 控制項」註冊成白名單：
			// 只有這些才允許輸出 on/off，避免主選單/清單/行為按鈕被誤加狀態。
			Main.Instance?.RegisterKnownSettingsToggles(__instance);
			// 分類切換時，標題文字常在同一個 header 上「晚一點才更新」，
			// 若用全場景掃描很容易唸到上一個分類。這裡把 state instance 交給 mod，
			// 讓它以該 state 所在 Canvas 範圍做掃描，並等文字穩定後再唸。
			Main.Instance?.RequestSpeakSettingsCategoryTitle(__instance);

			// 設定頁的「群組/標題」很多是 Prefab + Localize 靜態文字，不一定會走 TMP_Text.set_text。
			// 在畫面啟用後的短窗口內，用 heuristic 掃 TMP_Text，把 instanceId 加入 _settingsTmpTextIds。
			BeginSettingsPrefabCapture(__instance);
		}
		catch { }
	}

	private static void SettingsCategoryState_OnStateEnd_Postfix(object __instance)
	{
		try
		{
			// 離開設定頁時清掉白名單，避免回到主選單時又被當成開關輸出 on/off。
			Main.Instance?.ClearKnownSettingsToggles();
			// 避免 instanceId 重用造成後續誤朗讀
			_settingsTmpTextIds.Clear();
			_settingsCaptureRootGo = null;
			_settingsCaptureUntil = DateTime.MinValue;
		}
		catch { }
	}

	private static void Slider_set_value_Postfix(object __instance, float __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.OnSliderValueChanged(__instance);
		}
		catch { }
	}

	private static void Slider_SetValueWithoutNotify_Postfix(object __instance, float __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.OnSliderValueChanged(__instance);
		}
		catch { }
	}

	private static void Slider_Set_Internal_Postfix(object __instance, float __0, bool __1)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// __1 代表 sendCallback；就算是 false 我們也只會在「使用者剛進頁/剛選取」之後才唸（mod 端會節流）。
			Main.Instance?.OnSliderValueChanged(__instance);
		}
		catch { }
	}

	private static void Toggle_set_isOn_Postfix(object __instance, bool __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.OnToggleValueChanged(__instance);
		}
		catch { }
	}

	private static void Toggle_SetIsOnWithoutNotify_Postfix(object __instance, bool __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.OnToggleValueChanged(__instance);
		}
		catch { }
	}

	private static void Toggle_Set_Internal_Postfix(object __instance, bool __0, bool __1)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.OnToggleValueChanged(__instance);
		}
		catch { }
	}

	private static void PatchMornUguitoggles(HarmonyLib.Harmony harmony)
	{
		int patched = 0;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type[] types;
			try { types = asm.GetTypes(); }
			catch { continue; }

			foreach (var t in types)
			{
				try
				{
					var n = t.FullName ?? "";
					if (n.IndexOf("MornUGUI", StringComparison.OrdinalIgnoreCase) < 0) continue;
					if (n.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) < 0) continue;

					// 需要有 bool 的 IsOn/isOn 屬性才像 toggle
					var pIsOn = t.GetProperty("IsOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					          ?? t.GetProperty("isOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (pIsOn == null || pIsOn.PropertyType != typeof(bool)) continue;

					MethodInfo post = typeof(Patches).GetMethod("MornToggle_IsOnSetter_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
					if (post == null) continue;

					// patch setter
					var setMi = pIsOn.GetSetMethod(true);
					if (setMi != null)
					{
						harmony.Patch(setMi, null, new HarmonyMethod(post), null, null, null);
						patched++;
					}

					// patch 常見 no-notify / internal set
					var mNoNotify = t.GetMethod("SetIsOnWithoutNotify", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (mNoNotify != null)
					{
						harmony.Patch(mNoNotify, null, new HarmonyMethod(post), null, null, null);
						patched++;
					}
					var mSet = t.GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
					if (mSet != null)
					{
						harmony.Patch(mSet, null, new HarmonyMethod(post), null, null, null);
						patched++;
					}
				}
				catch { }
			}
		}

		if (patched > 0)
			MelonLogger.Msg("TolkExporter: patched MornUGUI toggles (dynamic) count=" + patched);
	}

	// 動態 toggle patch：不依賴具體型別名稱（MornUGUI.dll 內部實作可能會改）
	private static void MornToggle_IsOnSetter_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.OnToggleValueChanged(__instance);
		}
		catch { }
	}


	private static void UIHubTips_SetUp_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			object obj = __instance.GetType().GetField("_descriptionText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			if (obj != null)
			{
				string text = obj.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) as string;
				if (!string.IsNullOrWhiteSpace(text))
				{
					string spoken = HubHandler.BuildTipAnnouncement(text);
					if (!string.IsNullOrWhiteSpace(spoken))
					{
						Main.Instance?.SendToTolk(spoken);
					}
				}
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: UIHubTips_Postfix failed: " + ex);
		}
	}

	private static void UIWorldMapDetail_SetUpDetail_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			string text = null;
			string text2 = null;
			string text3 = null;
			string text4 = null;
			Type type = __instance.GetType();
			try
			{
				object obj = type.GetField("_areaNumLabel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				text = (obj?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(obj) as string;
			}
			catch
			{
			}
			try
			{
				object obj3 = type.GetField("_areaLabel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				text2 = (obj3?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(obj3) as string;
			}
			catch
			{
			}
			try
			{
				object obj5 = type.GetField("_gameLabel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				text3 = (obj5?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(obj5) as string;
			}
			catch
			{
			}
			try
			{
				object obj7 = type.GetField("_difficultyLabel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				text4 = (obj7?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(obj7) as string;
			}
			catch
			{
			}
			List<string> list = new List<string>();
			if (!string.IsNullOrWhiteSpace(text))
			{
				list.Add(text.Trim() + "：");
			}
			if (!string.IsNullOrWhiteSpace(text2))
			{
				list.Add(text2.Trim());
			}
			if (!string.IsNullOrWhiteSpace(text3))
			{
				list.Add(text3.Trim());
			}
			string text5 = string.Join("", list);
			if (!string.IsNullOrWhiteSpace(text4))
			{
				text5 += text4.Trim();
			}
			if (!string.IsNullOrWhiteSpace(text5))
			{
				Main.Instance?.SendToTolk(text5);
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: UIWorldMapDetail_Postfix failed: " + ex);
		}
	}

	private static void WorldMapParentUI_Start_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// 直接讀 UI 上實際顯示的文字（已本地化）
			object tmp = __instance.GetType().GetField("_worldName", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			string s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (string.IsNullOrWhiteSpace(s)) return;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastWorldMapName, s, StringComparison.Ordinal) && (now - _lastWorldMapNameAt).TotalMilliseconds < 800)
				return;
			_lastWorldMapName = s;
			_lastWorldMapNameAt = now;

			Main.Instance?.SendToTolk(s);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: WorldMapParentUI_Start_Postfix failed: " + ex);
		}
	}

	private static void WorldMapMoveState_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;

			// 找到 WorldMapParentUI，直接讀 UI 上顯示的地圖名（已本地化）
			Type t = AccessTools.TypeByName("PastelParade.WorldMapParentUI");
			if (t == null) return;
			Array arr = FindUnityObjectsByType(t);
			if (arr == null || arr.Length == 0) return;

			string s = null;
			for (int i = 0; i < arr.Length; i++)
			{
				var ui = arr.GetValue(i);
				if (ui == null) continue;
				object tmp = ui.GetType().GetField("_worldName", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ui);
				if (tmp == null) continue;
				if (!IsComponentActive(tmp)) continue;
				s = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
				s = StripTmpRichText(s).Trim();
				if (!string.IsNullOrWhiteSpace(s)) break;
			}
			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastWorldMapName, s, StringComparison.Ordinal) && (now - _lastWorldMapNameAt).TotalMilliseconds < 800)
				return;
			_lastWorldMapName = s;
			_lastWorldMapNameAt = now;

			Main.Instance?.SendToTolk(s);
		}
		catch { }
	}

	private static void WorldMapMoveState_OnStateUpdate_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// 直接抓鍵盤的 [ / ]（Unity 6000 多半走新 InputSystem；legacy Input.GetKeyDown 可能抓不到）
			// 同時保留遊戲 mapping（TabLeft/TabRight）當作 fallback
			object input = __instance.GetType().GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			bool prev = IsInputSystemKeyPressedThisFrame("leftBracketKey") || IsLegacyKeyDown("LeftBracket") || GetBoolProperty(input, "TabLeft");
			bool next = IsInputSystemKeyPressedThisFrame("rightBracketKey") || IsLegacyKeyDown("RightBracket") || GetBoolProperty(input, "TabRight");
			if (!next && !prev) return;

			var now = DateTime.Now;
			if ((now - _worldMapCycleLastAt).TotalMilliseconds < 120)
				return;
			_worldMapCycleLastAt = now;

			// Get SaveData current
			object saveManager = __instance.GetType().GetField("_saveManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			object saveData = saveManager?.GetType().GetProperty("Current", BindingFlags.Instance | BindingFlags.Public)?.GetValue(saveManager);
			if (saveData == null) return;

			string lang = null;
			try { lang = saveData.GetType().GetField("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(saveData) as string; } catch { lang = null; }
			if (string.IsNullOrWhiteSpace(lang))
				try { lang = saveData.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(saveData) as string; } catch { lang = null; }

			// Get player
			object player = null;
			try
			{
				var p = __instance.GetType().GetProperty("Player", BindingFlags.Instance | BindingFlags.NonPublic);
				player = p?.GetValue(__instance);
			}
			catch { player = null; }
			if (player == null)
			{
				var container = __instance.GetType().GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				player = container?.GetType().GetProperty("Player", BindingFlags.Instance | BindingFlags.Public)?.GetValue(container);
			}
			if (player == null) return;

			// Scan interactables
			Type itType = AccessTools.TypeByName("PastelParade.WorldMapInteractableMono");
			if (itType == null) return;
			Array arr = FindUnityObjectsByType(itType);
			if (arr == null || arr.Length == 0) return;

			var list = new List<(object it, float x, float y, int id)>();
			for (int i = 0; i < arr.Length; i++)
			{
				var it = arr.GetValue(i);
				if (it == null) continue;

				// only active in hierarchy
				var go = it.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(it);
				if (go == null) continue;
				var aihObj = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
				if (aihObj is bool aih && !aih) continue;

				// exclude internal/dummy interactables by name (user reported: moveMN / movdAK)
				try
				{
					var n = go.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go) as string;
					if (IsWorldMapCycleExcludedName(n)) continue;
				}
				catch { }

				// collider enabled indicates spawn/active
				try
				{
					var col = it.GetType().GetField("_collider", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(it);
					var enObj = col?.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public)?.GetValue(col);
					if (enObj is bool en && !en) continue;
				}
				catch { }

				// WorldMapInteractableMono 包含所有交互點（關卡 / 小說 / 換區 / 物件等）
				var info = it.GetType().GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(it);
				if (info == null) continue;

				// CanFocus(save)
				try
				{
					var mi = info.GetType().GetMethod("CanFocus", BindingFlags.Instance | BindingFlags.Public);
					if (mi != null)
					{
						var ok = mi.Invoke(info, new object[] { saveData });
						if (ok is bool b && !b) continue;
					}
				}
				catch { }

				int id = GetUnityInstanceId(it);
				if (id == 0) continue;

				float x = 0f, y = 0f;
				try
				{
					var tr = it.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(it);
					var pos = tr?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
					if (pos != null)
					{
						x = Convert.ToSingle(pos.GetType().GetField("x")?.GetValue(pos) ?? 0f);
						y = Convert.ToSingle(pos.GetType().GetField("y")?.GetValue(pos) ?? 0f);
					}
				}
				catch { }

				list.Add((it, x, y, id));
			}
			if (list.Count == 0) return;

			// stable ordering: top-to-bottom then left-to-right
			list.Sort((a, b) =>
			{
				int cy = (-a.y).CompareTo(-b.y);
				if (cy != 0) return cy;
				return a.x.CompareTo(b.x);
			});

			int curIdx = 0;
			if (_worldMapCycleCurrentInteractableId != 0)
			{
				for (int i = 0; i < list.Count; i++)
					if (list[i].id == _worldMapCycleCurrentInteractableId) { curIdx = i; break; }
			}
			else
			{
				// find nearest to player position as initial
				float px = 0f, py = 0f;
				try
				{
					var pos = player.GetType().GetProperty("Position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(player);
					if (pos != null)
					{
						px = Convert.ToSingle(pos.GetType().GetField("x")?.GetValue(pos) ?? 0f);
						py = Convert.ToSingle(pos.GetType().GetField("y")?.GetValue(pos) ?? 0f);
					}
				}
				catch { }
				double best = double.MaxValue;
				for (int i = 0; i < list.Count; i++)
				{
					double dx = list[i].x - px;
					double dy = list[i].y - py;
					double d2 = dx * dx + dy * dy;
					if (d2 < best) { best = d2; curIdx = i; }
				}
			}

			int nextIdx = curIdx + (next ? 1 : -1);
			if (nextIdx < 0) nextIdx = list.Count - 1;
			if (nextIdx >= list.Count) nextIdx = 0;

			var target = list[nextIdx];
			_worldMapCycleCurrentInteractableId = target.id;

			// WorldMap 的「指針/焦點」由玩家(WorldMapChara)的 _interactable 決定（原始碼：OnTriggerEnter2D/OnTriggerExit2D）
			// 這裡直接套用同樣的狀態變更，而不是依賴物理觸發。
			object oldInteractable = null;
			bool isColliderActive = true;
			try
			{
				var fInter = player.GetType().GetField("_interactable", BindingFlags.Instance | BindingFlags.NonPublic);
				oldInteractable = fInter?.GetValue(player);

				var col = player.GetType().GetField("_collider", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(player);
				var enObj = col?.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public)?.GetValue(col);
				if (enObj is bool en) isColliderActive = en;

				// unfocus old
				if (oldInteractable != null && !ReferenceEquals(oldInteractable, target.it))
				{
					var mUn = oldInteractable.GetType().GetMethod("OnUnFocus", BindingFlags.Instance | BindingFlags.Public);
					mUn?.Invoke(oldInteractable, new object[] { isColliderActive });
				}

				// focus new + set player's interactable pointer
				var mFocus = target.it.GetType().GetMethod("OnFocus", BindingFlags.Instance | BindingFlags.Public);
				mFocus?.Invoke(target.it, new object[] { true });
				fInter?.SetValue(player, target.it);
			}
			catch (Exception ex)
			{
				// 這裡失敗會導致「看起來沒移動/沒反應」，所以至少留一行警告好追
				try { MelonLogger.Warning("TolkExporter: world map cycle focus failed: " + ex.GetType().Name); } catch { }
			}

			// Move in-game pointer: teleport player to the interactable position (direct)
			try
			{
				var mi = player.GetType().GetMethod("SetPosition", BindingFlags.Instance | BindingFlags.Public);
				if (mi != null)
				{
					// build Vector2 using the method parameter type (most reliable in Unity/Mono)
					var ps = mi.GetParameters();
					if (ps != null && ps.Length == 1)
					{
						var v2Type = ps[0].ParameterType;
						object v2 = Activator.CreateInstance(v2Type, new object[] { target.x, target.y });
						mi.Invoke(player, new[] { v2 });
					}
				}

				// ensure player stops immediately at the target
				var mStop = player.GetType().GetMethod("StopMove", BindingFlags.Instance | BindingFlags.Public);
				mStop?.Invoke(player, null);
			}
			catch { }

			// try to sync transforms so triggers/focus feel immediate
			try
			{
				var phys2dType = Type.GetType("UnityEngine.Physics2D, UnityEngine.Physics2DModule") ?? Type.GetType("UnityEngine.Physics2D");
				var mSync = phys2dType?.GetMethod("SyncTransforms", BindingFlags.Static | BindingFlags.Public);
				mSync?.Invoke(null, null);
			}
			catch { }

			// Speak selected stage once
			try
			{
				string s = BuildWorldMapInteractableSpeakText(target.it, lang, saveData);
				if (!string.IsNullOrWhiteSpace(s))
					Main.Instance?.SendToTolk(s);
			}
			catch { }
		}
		catch { }
	}

	private static string BuildWorldMapInteractableSpeakText(object interactable, string lang, object saveData)
	{
		try
		{
			if (interactable == null) return null;
			var info = interactable.GetType().GetField("_info", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(interactable);
			if (info == null) return null;
			lang = (lang ?? "").Trim();
			if (lang.Length == 0) lang = "en";

			// Area move: 用遊戲 UI 用的同一個本地化字串（WorldMapAreaMoveUIState: info.AreaMoveText.Get(CurrentLanguage)）
			try
			{
				var pAreaMove = info.GetType().GetProperty("IsAreaMove", BindingFlags.Instance | BindingFlags.Public);
				if (pAreaMove != null && (bool)pAreaMove.GetValue(info))
				{
					var pText = info.GetType().GetProperty("AreaMoveText", BindingFlags.Instance | BindingFlags.Public);
					var loc = pText?.GetValue(info);
					if (loc != null)
					{
						var mGet = loc.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
						var s2 = mGet?.Invoke(loc, new object[] { lang }) as string;
						s2 = StripTmpRichText(s2 ?? "").Trim();
						if (!string.IsNullOrWhiteSpace(s2)) return s2;
					}
				}
			}
			catch { }

			// Rhythm game: 使用 UIWorldMapDetail 顯示的資料（GameDetailSo）
			var pRhythm = info.GetType().GetProperty("IsRhythmGame", BindingFlags.Instance | BindingFlags.Public);
			if (pRhythm != null && (bool)pRhythm.GetValue(info))
			{
				var pDetails = info.GetType().GetProperty("Details", BindingFlags.Instance | BindingFlags.Public);
				var detailsArr = pDetails?.GetValue(info) as Array;
				if (detailsArr != null && detailsArr.Length > 0)
				{
					// pick first unlocked, else first
					object chosen = detailsArr.GetValue(0);
					try
					{
						var miUnlocked = chosen?.GetType().GetMethod("IsUnlocked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (miUnlocked != null && saveData != null)
						{
							for (int i = 0; i < detailsArr.Length; i++)
							{
								var d = detailsArr.GetValue(i);
								if (d == null) continue;
								var ok = miUnlocked.Invoke(d, new object[] { saveData });
								if (ok is bool b && b) { chosen = d; break; }
							}
						}
					}
					catch { }
					if (chosen != null)
					{
						string areaNum = null, area = null, music = null, diff = null;
						try { areaNum = chosen.GetType().GetField("AreaNumName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(chosen) as string; } catch { }
						try
						{
							var mi = chosen.GetType().GetMethod("GameName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							area = mi?.Invoke(chosen, new object[] { lang }) as string;
						}
						catch { }
						try
						{
							var mi = chosen.GetType().GetMethod("MusicName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							music = mi?.Invoke(chosen, new object[] { lang }) as string;
						}
						catch { }
						try
						{
							var mi = chosen.GetType().GetMethod("GetDifficulty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							diff = mi?.Invoke(chosen, null) as string;
						}
						catch { }

						areaNum = StripTmpRichText(areaNum ?? "").Trim();
						area = StripTmpRichText(area ?? "").Trim();
						music = StripTmpRichText(music ?? "").Trim();
						diff = StripTmpRichText(diff ?? "").Trim();

						var parts = new List<string>();
						if (!string.IsNullOrWhiteSpace(areaNum)) parts.Add(areaNum);
						if (!string.IsNullOrWhiteSpace(area)) parts.Add(area);
						if (!string.IsNullOrWhiteSpace(music)) parts.Add(music);
						if (!string.IsNullOrWhiteSpace(diff)) parts.Add(diff);
						var s = string.Join("：", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
						if (!string.IsNullOrWhiteSpace(s)) return s;
					}
				}
			}

			// Fallback: 沒有可用的顯示文字時，至少回報 object 名稱（避免完全無聲）
			try
			{
				var go = interactable.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(interactable);
				var n = go?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go) as string;
				n = StripTmpRichText(n ?? "").Trim();
				if (IsWorldMapCycleExcludedName(n)) return null;
				return string.IsNullOrWhiteSpace(n) ? null : n;
			}
			catch { }
			return null;
		}
		catch { return null; }
	}

	private static bool IsWorldMapCycleExcludedName(string name)
	{
		try
		{
			name = (name ?? "").Trim();
			if (name.Length == 0) return false;
			return string.Equals(name, "moveMN", StringComparison.OrdinalIgnoreCase)
			       || string.Equals(name, "movdAK", StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	private static bool GetBoolProperty(object obj, string propertyName)
	{
		try
		{
			if (obj == null || string.IsNullOrWhiteSpace(propertyName)) return false;
			var t = obj.GetType();
			var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (p != null && p.PropertyType == typeof(bool))
				return (bool)p.GetValue(obj);
			var m = t.GetMethod("get_" + propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (m != null && m.ReturnType == typeof(bool))
				return (bool)m.Invoke(obj, null);
			return false;
		}
		catch { return false; }
	}

	private static bool IsLegacyKeyDown(string keyCodeName)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(keyCodeName)) return false;
			var inputType = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule") ?? Type.GetType("UnityEngine.Input");
			var keyCodeType = Type.GetType("UnityEngine.KeyCode, UnityEngine.CoreModule") ?? Type.GetType("UnityEngine.KeyCode");
			if (inputType == null || keyCodeType == null) return false;
			var m = inputType.GetMethod("GetKeyDown", BindingFlags.Static | BindingFlags.Public, null, new[] { keyCodeType }, null);
			if (m == null) return false;
			var kc = Enum.Parse(keyCodeType, keyCodeName, ignoreCase: true);
			var v = m.Invoke(null, new[] { kc });
			return v is bool b && b;
		}
		catch { return false; }
	}

	private static bool IsInputSystemKeyPressedThisFrame(string keyboardKeyPropertyName)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(keyboardKeyPropertyName)) return false;
			// Unity Input System: UnityEngine.InputSystem.Keyboard.current.leftBracketKey.wasPressedThisFrame
			var keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
			if (keyboardType == null) return false;
			var pCurrent = keyboardType.GetProperty("current", BindingFlags.Static | BindingFlags.Public);
			var keyboard = pCurrent?.GetValue(null);
			if (keyboard == null) return false;
			var pKey = keyboard.GetType().GetProperty(keyboardKeyPropertyName, BindingFlags.Instance | BindingFlags.Public);
			var key = pKey?.GetValue(keyboard);
			if (key == null) return false;
			var pPressed = key.GetType().GetProperty("wasPressedThisFrame", BindingFlags.Instance | BindingFlags.Public);
			var v = pPressed?.GetValue(key);
			return v is bool b && b;
		}
		catch { return false; }
	}

	private static void UITimingSettingState_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// register mapping: timing slider -> timing text
			object slider = __instance.GetType().GetField("_timingSlider", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			object timingText = __instance.GetType().GetField("_timingText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			int sid = GetUnityInstanceId(slider);
			if (sid != 0 && timingText != null)
			{
				_timingTextBySliderId[sid] = timingText;
				int tid = GetUnityInstanceId(timingText);
				if (tid != 0) _timingTmpTextIds.Add(tid);
			}

			// speak current timing text once on enter
			string s = timingText?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(timingText) as string;
			s = StripTmpRichText(s ?? "").Trim();
			if (!string.IsNullOrWhiteSpace(s))
			{
				var now = DateTime.Now;
				if (!(string.Equals(_lastTimingSettingText, s, StringComparison.Ordinal) && (now - _lastTimingSettingTextAt).TotalMilliseconds < 500))
				{
					_lastTimingSettingText = s;
					_lastTimingSettingTextAt = now;
					Main.Instance?.SendToTolk(s);
				}
			}

			// 正確 hook：提示文字是 UI Prefab 內的 TMP，進場時會被啟用（OnEnable / SetActive），不是程式設字。
			// 因此這裡只設定「短窗口」與「根範圍」，由 TMP_Text.OnEnable hook 實際朗讀。
			BeginTimingHintCaptureWindow(timingText);
		}
		catch { }
	}

	private static void UITimingSettingState_OnStateEnd_Postfix(object __instance)
	{
		try
		{
			if (__instance == null) return;
			object slider = __instance.GetType().GetField("_timingSlider", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			object timingText = __instance.GetType().GetField("_timingText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			int sid = GetUnityInstanceId(slider);
			if (sid != 0)
				_timingTextBySliderId.Remove(sid);
			// clear all timing screen registered TMP ids (value + hint etc.)
			_timingTmpTextIds.Clear();
			_timingHintCaptureRootGo = null;
			_timingHintCaptureUntil = DateTime.MinValue;
			_timingHintCaptureValueTextId = 0;
		}
		catch { }
	}

	private static void BeginTimingHintCaptureWindow(object timingValueText)
	{
		try
		{
			if (timingValueText == null) return;
			var go = timingValueText.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(timingValueText);
			if (go == null) return;

			// 以同一個 Canvas 內的文字為範圍（提示文字屬於 UI 資產）
			var canvasGo = FindTopmostCanvasGo(go);
			_timingHintCaptureRootGo = canvasGo ?? go;
			_timingHintCaptureUntil = DateTime.Now.AddMilliseconds(1200);
			_timingHintCaptureValueTextId = GetUnityInstanceId(timingValueText);
		}
		catch { }
	}

	private static void TMPText_OnEnable_TimingHint_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// Only handle TMP text instances (we patch MaskableGraphic.OnEnable)
			try
			{
				Type tmpTextType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
				if (tmpTextType == null || !tmpTextType.IsInstanceOfType(__instance)) return;
			}
			catch { return; }

			// Timing hint (Adjust Timing)
			if (_timingHintCaptureRootGo != null && DateTime.Now <= _timingHintCaptureUntil)
			{
				TrySpeakPrefabTextOnEnable(
					__instance,
					_timingHintCaptureRootGo,
					ref _timingHintCaptureUntil,
					ref _lastTimingHintText,
					ref _lastTimingHintAt,
					_timingHintCaptureValueTextId,
					_timingTmpTextIds,
					closeAfterFirst: true,
					speakAsDialogBodyOnce: true
				);
			}

			// Startup screen (Title)
			if (_startupCaptureRootGo != null && DateTime.Now <= _startupCaptureUntil)
			{
				TrySpeakPrefabTextOnEnable(
					__instance,
					_startupCaptureRootGo,
					ref _startupCaptureUntil,
					ref _lastStartupSpoken,
					ref _lastStartupSpokenAt,
					skipId: 0,
					registerSet: _startupTmpTextIds,
					closeAfterFirst: false
				);
				RegisterHeuristicTmpTextIdsUnderRoot(_startupCaptureRootGo, _startupTmpTextIds, ref _startupLastHeuristicScanAt);
			}

			// Level loading screen (GameTransition overlay)
			if (_loadingCaptureRootGo != null && DateTime.Now <= _loadingCaptureUntil)
			{
				TrySpeakPrefabTextOnEnable(
					__instance,
					_loadingCaptureRootGo,
					ref _loadingCaptureUntil,
					ref _lastLoadingSpoken,
					ref _lastLoadingSpokenAt,
					skipId: 0,
					registerSet: _loadingTmpTextIds,
					closeAfterFirst: false
				);
				RegisterHeuristicTmpTextIdsUnderRoot(_loadingCaptureRootGo, _loadingTmpTextIds, ref _loadingLastHeuristicScanAt);
			}

			// Settings pages
			if (_settingsCaptureRootGo != null && DateTime.Now <= _settingsCaptureUntil)
			{
				// do not auto-speak here; just make sure ids are registered for (possible) Localize updates
				TryRegisterPrefabTextIdOnEnable(__instance, _settingsCaptureRootGo, _settingsTmpTextIds);
				RegisterHeuristicTmpTextIdsUnderRoot(_settingsCaptureRootGo, _settingsTmpTextIds, ref _settingsLastHeuristicScanAt);
			}
		}
		catch { }
	}

	private static void TryRegisterPrefabTextIdOnEnable(object tmp, object captureRootGo, HashSet<int> registerSet)
	{
		try
		{
			if (tmp == null || captureRootGo == null || registerSet == null) return;
			if (!IsComponentActive(tmp)) return;
			var go = tmp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tmp);
			if (go == null) return;
			if (!IsSameOrDescendant(go, captureRootGo, 40)) return;

			// Exclude selectable/button labels
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Selectable");
			if (selectableType != null)
			{
				var hasSel = FindComponentInParents(go, selectableType, 12);
				if (hasSel != null) return;
			}

			int id = GetUnityInstanceId(tmp);
			if (id == 0) return;
			string text = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (!IsHeuristicStaticTmpText(text)) return;
			registerSet.Add(id);
		}
		catch { }
	}

	private static void TrySpeakPrefabTextOnEnable(
		object tmp,
		object captureRootGo,
		ref DateTime captureUntil,
		ref string lastText,
		ref DateTime lastAt,
		int skipId,
		HashSet<int> registerSet,
		bool closeAfterFirst,
		bool speakAsDialogBodyOnce = false
	)
	{
		try
		{
			if (tmp == null || captureRootGo == null) return;
			if (DateTime.Now > captureUntil) return;
			if (!IsComponentActive(tmp)) return;

			var go = tmp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tmp);
			if (go == null) return;
			if (!IsSameOrDescendant(go, captureRootGo, 40)) return;

			int id = GetUnityInstanceId(tmp);
			if (id == 0) return;
			if (skipId != 0 && id == skipId) return;

			// Exclude selectable/button labels
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Selectable");
			if (selectableType != null)
			{
				var hasSel = FindComponentInParents(go, selectableType, 12);
				if (hasSel != null) return;
			}

			string text = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			text = StripTmpRichText(text ?? "").Trim();
			if (!IsHeuristicStaticTmpText(text)) return;

			// register id so Localize updates (if any) will be spoken later
			try { registerSet?.Add(id); } catch { }

			// speak (dedupe)
			var now = DateTime.Now;
			if (string.Equals(lastText, text, StringComparison.Ordinal) && (now - lastAt).TotalMilliseconds < 1500)
				return;
			lastText = text;
			lastAt = now;
			if (speakAsDialogBodyOnce)
				Main.Instance?.SpeakDialogBodyOnceDelayed(text, 1400);
			else
				Main.Instance?.SendToTolk(text);

			if (closeAfterFirst)
				captureUntil = DateTime.MinValue;
		}
		catch { }
	}

	private static void BeginStartupPrefabCapture()
	{
		try
		{
			_startupTmpTextIds.Clear();

			// prefer current selection's canvas; fallback to any TMP on screen
			object go = GetCurrentSelectedGameObject();
			if (go == null)
			{
				Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
				var arr = FindUnityObjectsByType(tmpType);
				if (arr != null && arr.Length > 0) go = arr.GetValue(0)?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(arr.GetValue(0));
			}
			if (go == null) return;

			var canvasGo = FindTopmostCanvasGo(go);
			_startupCaptureRootGo = canvasGo ?? go;
			_startupCaptureUntil = DateTime.Now.AddMilliseconds(1800);
			RegisterHeuristicTmpTextIdsUnderRoot(_startupCaptureRootGo, _startupTmpTextIds, ref _startupLastHeuristicScanAt);
			// 立即從 Title UI Prefab 的最終顯示文字朗讀（不依賴 OnEnable / set_text）
			TrySpeakBestNonSelectableTmpUnderRoot(_startupCaptureRootGo, ref _lastStartupSpoken, ref _lastStartupSpokenAt);
		}
		catch { }
	}

	private static void BeginLoadingPrefabCapture(object anyComponent)
	{
		try
		{
			_loadingTmpTextIds.Clear();

			object go = anyComponent?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(anyComponent);
			if (go == null) return;
			var canvasGo = FindTopmostCanvasGo(go);
			_loadingCaptureRootGo = canvasGo ?? go;
			_loadingCaptureUntil = DateTime.Now.AddMilliseconds(2200);
			RegisterHeuristicTmpTextIdsUnderRoot(_loadingCaptureRootGo, _loadingTmpTextIds, ref _loadingLastHeuristicScanAt);
			// 立即從 Loading UI Prefab 的最終顯示文字朗讀（不依賴 OnEnable / set_text）
			TrySpeakBestNonSelectableTmpUnderRoot(_loadingCaptureRootGo, ref _lastLoadingSpoken, ref _lastLoadingSpokenAt);
		}
		catch { }
	}

	private static void BeginSettingsPrefabCapture(object anyStateInstance)
	{
		try
		{
			if (anyStateInstance == null) return;
			var go = anyStateInstance.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(anyStateInstance);
			if (go == null) return;
			var canvasGo = FindTopmostCanvasGo(go);
			_settingsCaptureRootGo = canvasGo ?? go;
			_settingsCaptureUntil = DateTime.Now.AddMilliseconds(1800);
			RegisterHeuristicTmpTextIdsUnderRoot(_settingsCaptureRootGo, _settingsTmpTextIds, ref _settingsLastHeuristicScanAt);
		}
		catch { }
	}

	private static void RegisterAllTmpTextIdsUnderRoot(object rootGo, HashSet<int> outSet)
	{
		try
		{
			if (rootGo == null || outSet == null) return;
			Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
			if (tmpType == null) return;
			var mi = rootGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
			var arr = mi?.Invoke(rootGo, new object[] { tmpType, true }) as Array;
			if (arr == null) return;
			for (int i = 0; i < arr.Length; i++)
			{
				var c = arr.GetValue(i);
				if (c == null) continue;
				int id = GetUnityInstanceId(c);
				if (id != 0) outSet.Add(id);
			}
		}
		catch { }
	}

	private static bool IsHeuristicStaticTmpText(string text)
	{
		try
		{
			text = StripTmpRichText(text ?? "").Trim();
			if (string.IsNullOrWhiteSpace(text)) return false;

			// Avoid obvious values
			if (text.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) return false;
			if (text.Contains("%") || text.Contains("ms") || text.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || text.Contains(" x "))
				return false;

			// reasonable length (allow short titles like "音量")
			if (text.Length < 2) return false;
			if (text.Length > 120) return false;

			// numeric-ish only
			bool allDigits = true;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (char.IsDigit(c) || c == '+' || c == '-' || c == '.' || c == '%' || c == '/' || c == ':' || c == ' ') continue;
				allDigits = false; break;
			}
			if (allDigits) return false;

			return true;
		}
		catch { return false; }
	}

	private static void RegisterHeuristicTmpTextIdsUnderRoot(object rootGo, HashSet<int> targetSet, ref DateTime lastScanAt)
	{
		try
		{
			if (rootGo == null || targetSet == null) return;
			var now = DateTime.Now;
			if ((now - lastScanAt).TotalMilliseconds < 150) return;
			lastScanAt = now;

			Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
			if (tmpType == null) return;
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Selectable");

			var mi = rootGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
			var arr = mi?.Invoke(rootGo, new object[] { tmpType, true }) as Array;
			if (arr == null || arr.Length == 0) return;

			for (int i = 0; i < arr.Length; i++)
			{
				var c = arr.GetValue(i);
				if (c == null) continue;
				if (!IsComponentActive(c)) continue;

				var go = c.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
				if (go == null) continue;

				// under selectable -> skip
				if (selectableType != null)
				{
					var has = FindComponentInParents(go, selectableType, 12);
					if (has != null) continue;
				}

				string s = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c) as string;
				if (!IsHeuristicStaticTmpText(s)) continue;

				int id = GetUnityInstanceId(c);
				if (id != 0) targetSet.Add(id);
			}
		}
		catch { }
	}

	private static void TrySpeakBestNonSelectableTmpUnderRoot(object rootGo, ref string lastText, ref DateTime lastAt)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (rootGo == null) return;
			Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
			if (tmpType == null) return;
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Selectable");

			var mi = rootGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
			var arr = mi?.Invoke(rootGo, new object[] { tmpType, true }) as Array;
			if (arr == null || arr.Length == 0) return;

			string best = null;
			float bestScore = float.NegativeInfinity;
			for (int i = 0; i < arr.Length; i++)
			{
				var c = arr.GetValue(i);
				if (c == null) continue;
				if (!IsComponentActive(c)) continue;

				var go = c.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
				if (go == null) continue;

				// under selectable -> skip (avoid menu items)
				if (selectableType != null)
				{
					var has = FindComponentInParents(go, selectableType, 12);
					if (has != null) continue;
				}

				var s = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c) as string;
				s = StripTmpRichText(s).Trim();
				if (string.IsNullOrWhiteSpace(s)) continue;

				// exclude obvious non-title values
				if (s.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) continue;
				if (s.Length > 80) continue;

				// score by fontSize + position.y (similar to title static selection)
				float fontSize = 0f;
				try
				{
					var fs = c.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					if (fs is float f) fontSize = f;
					else if (fs is double d) fontSize = (float)d;
				}
				catch { }
				float y = 0f;
				try
				{
					var rt = c.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					var pos = rt?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rt);
					var py = pos?.GetType().GetField("y")?.GetValue(pos);
					if (py is float fy) y = fy;
					else if (py is double dy) y = (float)dy;
				}
				catch { }
				float score = fontSize * 1000f + y;
				if (score > bestScore)
				{
					bestScore = score;
					best = s;
				}
			}

			if (string.IsNullOrWhiteSpace(best)) return;
			var now = DateTime.Now;
			if (string.Equals(lastText, best, StringComparison.Ordinal) && (now - lastAt).TotalMilliseconds < 1500)
				return;
			lastText = best;
			lastAt = now;
			Main.Instance?.SendToTolk(best);
		}
		catch { }
	}

	private static object FindTopmostCanvasGo(object anyGo)
	{
		try
		{
			if (anyGo == null) return null;
			Type canvasType = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule")
			                 ?? Type.GetType("UnityEngine.Canvas, UnityEngine.UI")
			                 ?? Type.GetType("UnityEngine.Canvas");
			if (canvasType == null) return null;

			object lastCanvasGo = null;
			object curGo = anyGo;
			int guard = 0;
			while (curGo != null && guard++ < 64)
			{
				if (TryGetComponent(curGo, canvasType) != null)
					lastCanvasGo = curGo;
				var tr = curGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(curGo);
				var parentTr = tr?.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				curGo = parentTr?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parentTr);
			}
			return lastCanvasGo;
		}
		catch { return null; }
	}

	private static bool IsSameOrDescendant(object childGo, object parentGo, int maxDepth)
	{
		try
		{
			if (childGo == null || parentGo == null) return false;
			if (ReferenceEquals(childGo, parentGo)) return true;
			object curGo = childGo;
			for (int i = 0; i < maxDepth; i++)
			{
				if (ReferenceEquals(curGo, parentGo)) return true;
				var tr = curGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(curGo);
				var parentTr = tr?.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				curGo = parentTr?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parentTr);
				if (curGo == null) break;
			}
		}
		catch { }
		return false;
	}

	private static object GetTopmostParentGameObject(object go)
	{
		try
		{
			if (go == null) return null;
			var tr = go.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
			if (tr == null) return go;
			int guard = 0;
			object lastGo = go;
			while (tr != null && guard++ < 80)
			{
				lastGo = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr) ?? lastGo;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
			return lastGo ?? go;
		}
		catch { return go; }
	}

	private static void PatchSettingsValueTextMapping(HarmonyLib.Harmony harmony, string typeName, string[] sliderFieldNames, string[] valueTextFieldNames)
	{
		try
		{
			Type t = AccessTools.TypeByName(typeName);
			if (t == null) return;

			MethodInfo mBegin = t.GetMethod("OnStateBegin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo postBegin = typeof(Patches).GetMethod("SettingsValueText_OnStateBegin_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (mBegin != null && postBegin != null)
			{
				harmony.Patch((MethodBase)mBegin, null, new HarmonyMethod(postBegin), null, null, null);
			}

			MethodInfo mEnd = t.GetMethod("OnStateEnd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo postEnd = typeof(Patches).GetMethod("SettingsValueText_OnStateEnd_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (mEnd != null && postEnd != null)
			{
				harmony.Patch((MethodBase)mEnd, null, new HarmonyMethod(postEnd), null, null, null);
			}

			// store field name pairs on the type via static dictionaries (key: type fullname)
			_settingsValueTextMapConfig[typeName] = (sliderFieldNames ?? Array.Empty<string>(), valueTextFieldNames ?? Array.Empty<string>());
		}
		catch { }
	}

	private static readonly Dictionary<string, (string[] sliders, string[] texts)> _settingsValueTextMapConfig =
		new Dictionary<string, (string[] sliders, string[] texts)>();

	private static void SettingsValueText_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			if (__instance == null) return;
			var tn = __instance.GetType().FullName ?? "";
			if (!_settingsValueTextMapConfig.TryGetValue(tn, out var cfg)) return;
			var sliders = cfg.sliders ?? Array.Empty<string>();
			var texts = cfg.texts ?? Array.Empty<string>();
			int n = Math.Min(sliders.Length, texts.Length);
			for (int i = 0; i < n; i++)
			{
				object slider = __instance.GetType().GetField(sliders[i], BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				object valueText = __instance.GetType().GetField(texts[i], BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				int sid = GetUnityInstanceId(slider);
				if (sid != 0 && valueText != null)
				{
					_settingsValueTextBySliderId[sid] = valueText;
					int tid = GetUnityInstanceId(valueText);
					if (tid != 0) _settingsTmpTextIds.Add(tid);
				}
			}
		}
		catch { }
	}

	private static void SettingsValueText_OnStateEnd_Postfix(object __instance)
	{
		try
		{
			if (__instance == null) return;
			var tn = __instance.GetType().FullName ?? "";
			if (!_settingsValueTextMapConfig.TryGetValue(tn, out var cfg)) return;
			var sliders = cfg.sliders ?? Array.Empty<string>();
			var texts = cfg.texts ?? Array.Empty<string>();
			for (int i = 0; i < sliders.Length; i++)
			{
				object slider = __instance.GetType().GetField(sliders[i], BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				object valueText = null;
				try
				{
					if (i < texts.Length)
						valueText = __instance.GetType().GetField(texts[i], BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				}
				catch { valueText = null; }
				int sid = GetUnityInstanceId(slider);
				if (sid != 0) _settingsValueTextBySliderId.Remove(sid);
				int tid = GetUnityInstanceId(valueText);
				if (tid != 0) _settingsTmpTextIds.Remove(tid);
			}
		}
		catch { }
	}

	private static void HubInteractState_OnStateUpdate_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// [ = prev, ] = next
			bool prev = IsInputSystemKeyPressedThisFrame("leftBracketKey") || IsLegacyKeyDown("LeftBracket");
			bool next = IsInputSystemKeyPressedThisFrame("rightBracketKey") || IsLegacyKeyDown("RightBracket");
			if (!next && !prev) return;

			var now = DateTime.Now;
			if ((now - _hubCycleLastAt).TotalMilliseconds < 120)
				return;
			_hubCycleLastAt = now;

			// get hub cursor
			object hubCursor = __instance.GetType().GetField("_hubCursor", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			if (hubCursor == null) return;

			// scan active HubInteractable
			Type itType = AccessTools.TypeByName("PastelParade.HubInteractable");
			if (itType == null) return;
			Array arr = FindUnityObjectsByType(itType);
			if (arr == null || arr.Length == 0) return;

			var list = new List<(object it, float x, float y, int id)>();
			for (int i = 0; i < arr.Length; i++)
			{
				var it = arr.GetValue(i);
				if (it == null) continue;

				var go = it.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(it);
				if (go == null) continue;
				var aihObj = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
				if (aihObj is bool aih && !aih) continue;

				int id = GetUnityInstanceId(it);
				if (id == 0) continue;

				float x = 0f, y = 0f;
				try
				{
					var tr = it.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(it);
					var pos = tr?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
					if (pos != null)
					{
						x = Convert.ToSingle(pos.GetType().GetField("x")?.GetValue(pos) ?? 0f);
						y = Convert.ToSingle(pos.GetType().GetField("y")?.GetValue(pos) ?? 0f);
					}
				}
				catch { }

				list.Add((it, x, y, id));
			}
			if (list.Count == 0) return;

			// stable ordering: top-to-bottom then left-to-right
			list.Sort((a, b) =>
			{
				int cy = (-a.y).CompareTo(-b.y);
				if (cy != 0) return cy;
				return a.x.CompareTo(b.x);
			});

			// current = cursor.Interactable if present
			int curIdx = 0;
			try
			{
				var p = hubCursor.GetType().GetProperty("Interactable", BindingFlags.Instance | BindingFlags.Public);
				var cur = p?.GetValue(hubCursor);
				int curId = GetUnityInstanceId(cur);
				if (curId != 0)
				{
					_hubCycleCurrentInteractableId = curId;
				}
			}
			catch { }

			if (_hubCycleCurrentInteractableId != 0)
			{
				for (int i = 0; i < list.Count; i++)
					if (list[i].id == _hubCycleCurrentInteractableId) { curIdx = i; break; }
			}
			else
			{
				// nearest to cursor position
				float cx = 0f, cy = 0f;
				try
				{
					var tr = hubCursor.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(hubCursor);
					var pos = tr?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
					if (pos != null)
					{
						cx = Convert.ToSingle(pos.GetType().GetField("x")?.GetValue(pos) ?? 0f);
						cy = Convert.ToSingle(pos.GetType().GetField("y")?.GetValue(pos) ?? 0f);
					}
				}
				catch { }
				double best = double.MaxValue;
				for (int i = 0; i < list.Count; i++)
				{
					double dx = list[i].x - cx;
					double dy = list[i].y - cy;
					double d2 = dx * dx + dy * dy;
					if (d2 < best) { best = d2; curIdx = i; }
				}
			}

			int nextIdx = curIdx + (next ? 1 : -1);
			if (nextIdx < 0) nextIdx = list.Count - 1;
			if (nextIdx >= list.Count) nextIdx = 0;

			var target = list[nextIdx];
			_hubCycleCurrentInteractableId = target.id;

			// move hub cursor directly: set _cachedPos + transform.position so CursorUpdate won't immediately snap back
			try
			{
				var fCached = hubCursor.GetType().GetField("_cachedPos", BindingFlags.Instance | BindingFlags.NonPublic);
				if (fCached != null)
				{
					var v2Type = fCached.FieldType;
					var v2 = Activator.CreateInstance(v2Type, new object[] { target.x, target.y });
					fCached.SetValue(hubCursor, v2);
				}
				var tr = hubCursor.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(hubCursor);
				if (tr != null)
				{
					var posProp = tr.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public);
					var v3Type = posProp?.PropertyType;
					if (v3Type != null)
					{
						var v3 = Activator.CreateInstance(v3Type, new object[] { target.x, target.y, 0f });
						posProp.SetValue(tr, v3);
					}
				}
			}
			catch { }

			// force focus selection: overwrite overlapping set and call UpdateClosestInteractable()
			try
			{
				var fOver = hubCursor.GetType().GetField("_overlappingInteractables", BindingFlags.Instance | BindingFlags.NonPublic);
				var set = fOver?.GetValue(hubCursor);
				set?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)?.Invoke(set, null);
				set?.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public)?.Invoke(set, new object[] { target.it });
				hubCursor.GetType().GetMethod("UpdateClosestInteractable", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(hubCursor, null);
			}
			catch { }
		}
		catch { }
	}

	private static void HubInteractable_OnFocus_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			string s = null;
			// prefer the TMP_Text that the game uses for the bubble label
			try
			{
				var tmp = __instance.GetType().GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
				s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			}
			catch { s = null; }

			s = StripTmpRichText(s ?? "").Trim();

			// fallback: TextOnly tips
			if (string.IsNullOrWhiteSpace(s))
			{
				try
				{
					var info = __instance.GetType().GetField("_objectInfo", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
					var loc = __instance.GetType().GetField("_localize", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
					var lang = loc?.GetType().GetProperty("CurrentLanguage", BindingFlags.Instance | BindingFlags.Public)?.GetValue(loc) as string;
					var tips = info?.GetType().GetProperty("Tips", BindingFlags.Instance | BindingFlags.Public)?.GetValue(info);
					var mGet = tips?.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);
					s = mGet?.Invoke(tips, new object[] { lang }) as string;
					s = StripTmpRichText(s ?? "").Trim();
				}
				catch { s = null; }
			}

			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastHubInteractableText, s, StringComparison.Ordinal) && (now - _lastHubInteractableTextAt).TotalMilliseconds < 500)
				return;
			_lastHubInteractableText = s;
			_lastHubInteractableTextAt = now;
			Main.Instance?.SendToTolk(s);
		}
		catch { }
	}

	private static void MusicSelectState_UpdateUI_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			object tmp = __instance.GetType().GetField("_header", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			string s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (string.IsNullOrWhiteSpace(s)) return;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastMusicSelectHeader, s, StringComparison.Ordinal) && (now - _lastMusicSelectHeaderAt).TotalMilliseconds < 800)
				return;
			_lastMusicSelectHeader = s;
			_lastMusicSelectHeaderAt = now;

			Main.Instance?.SendToTolk(s);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: MusicSelectState_UpdateUI_Postfix failed: " + ex);
		}
	}

	private static void WorldMapAreaMoveUIState_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			object tmp = __instance.GetType().GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			string s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (string.IsNullOrWhiteSpace(s)) return;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			if (string.Equals(_lastAreaMoveText, s, StringComparison.Ordinal) && (now - _lastAreaMoveTextAt).TotalMilliseconds < 800)
				return;
			_lastAreaMoveText = s;
			_lastAreaMoveTextAt = now;

			Main.Instance?.SendToTolk(s);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: WorldMapAreaMoveUIState_OnStateBegin_Postfix failed: " + ex);
		}
	}

	private static void UIHubNovelRow_SetData_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// 翻頁/切 tab 時 UIHubNovel 會對每一列呼叫 SetData；這裡只能在「目前焦點列」時才朗讀，
			// 否則會把整頁項目唸一遍。
			try
			{
				var selGo = GetCurrentSelectedGameObject();
				if (selGo == null) return;
				var rowType = __instance.GetType();
				var row = FindComponentInParents(selGo, rowType, 6);
				if (row == null || !ReferenceEquals(row, __instance)) return;
			}
			catch { return; }

			if (!(__instance.GetType().GetField("_musicNameTexts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance) is Array array))
			{
				return;
			}
			foreach (object item in array)
			{
				if (item != null)
				{
					string text = item.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item) as string;
					if (!string.IsNullOrWhiteSpace(text))
					{
						text = StripTmpRichText(text).Trim();
						if (string.IsNullOrWhiteSpace(text)) return;
						if (Main.Instance?.IsTabMergePending == true)
							Main.Instance?.SetPendingTabFocusText(text);
						else
							Main.Instance?.SendToTolk(text);
						break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: UIHubNovelRow_Postfix failed: " + ex);
		}
	}

	private static void UIHubGameRow_SetData_Postfix(object __instance)
	{
		try
		{
			// 翻頁/切 tab 時 UIHubGame 會對每一列呼叫 SetData；
			// Hub 選曲的「焦點變更」正確觸發點是 HubService.SetPreview（見 UIHubGame.UpdateUI 的 OnFocus），
			// 因此這裡不做朗讀，避免整頁洗語音。
			return;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: UIHubGameRow_Postfix failed: " + ex);
		}
	}

	private static void TitleInitializeState_OnStateBegin_Postfix()
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// 主選單剛進來時，主機通常會設定初始選取；這裡主動觸發一次讀取，修正「主選單沒反應」。
			Main.Instance?.TriggerReadSelection();

			// 遊戲啟動畫面/Title：UI Prefab + Localize 文字不一定會再 set_text，
			// 正確作法：直接讀取 Title UI Prefab 上 TMP 的「最終顯示文字」。
			BeginStartupPrefabCapture();
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: TitleInitializeState_OnStateBegin_Postfix failed: " + ex);
		}
	}

	private static void TrySpeakTitleStaticTexts()
	{
		try
		{
			// 找所有 TMP_Text（title 的大多是 TMP）
			Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
			if (tmpType == null) return;
			Array arr = FindUnityObjectsByType(tmpType);
			if (arr == null || arr.Length == 0) return;

			// 排除按鈕文字：Selectable covers Button/Toggle/Slider...
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI")
			                    ?? Type.GetType("UnityEngine.UI.Selectable");

			string best1 = null;
			float best1Score = float.NegativeInfinity;
			string best2 = null;
			float best2Score = float.NegativeInfinity;

			for (int i = 0; i < arr.Length; i++)
			{
				var c = arr.GetValue(i);
				if (c == null) continue;
				if (!IsComponentActive(c)) continue;

				var go = c.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
				if (go == null) continue;

				// under selectable -> skip
				if (selectableType != null)
				{
					var has = FindComponentInParents(go, selectableType, 12);
					if (has != null) continue;
				}

				var s = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c) as string;
				s = StripTmpRichText(s).Trim();
				if (string.IsNullOrWhiteSpace(s)) continue;

				// 避免把版本字/數值當標題
				if (s.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) continue;
				if (s.Contains("%") || s.Contains("ms") || s.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || s.Contains(" x "))
					continue;
				double _;
				if (double.TryParse(s, out _)) continue;

				// 太長的通常是說明段落，不是主畫面標題
				if (s.Length > 64) continue;

				// fontSize + y
				float fontSize = 0f;
				try
				{
					var fs = c.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					if (fs is float f) fontSize = f;
					else if (fs is double d) fontSize = (float)d;
				}
				catch { }

				float y = 0f;
				try
				{
					var rt = c.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					var pos = rt?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rt);
					var py = pos?.GetType().GetField("y")?.GetValue(pos);
					if (py is float fy) y = fy;
					else if (py is double dy) y = (float)dy;
				}
				catch { }

				float score = fontSize * 1000f + y;

				// keep top2 unique
				if (score > best1Score)
				{
					if (!string.Equals(s, best1, StringComparison.Ordinal))
					{
						best2 = best1;
						best2Score = best1Score;
					}
					best1 = s;
					best1Score = score;
				}
				else if (score > best2Score && !string.Equals(s, best1, StringComparison.Ordinal))
				{
					best2 = s;
					best2Score = score;
				}
			}

			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(best1)) parts.Add(best1);
			if (!string.IsNullOrWhiteSpace(best2)) parts.Add(best2);
			if (parts.Count == 0) return;

			var spoken = string.Join(" ", parts);
			var now = DateTime.Now;
			if (string.Equals(_lastTitleHeaderText, spoken, StringComparison.Ordinal) && (now - _lastTitleHeaderAt).TotalMilliseconds < 1200)
				return;
			_lastTitleHeaderText = spoken;
			_lastTitleHeaderAt = now;

			Main.Instance?.SendToTolk(spoken);
		}
		catch { }
	}

	private static void TalkPanelCtrl_ShowWaitInputIcon_Postfix(object __instance, bool showWaitInput)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (!showWaitInput) return;
			if (__instance == null) return;

			// 取 TalkPanelCtrl.Text (TMP_Text) 的 text
			object tmp = __instance.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance);
			string s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (string.IsNullOrWhiteSpace(s)) return;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			// 防止同一句在同一個 wait 狀態被重複觸發
			if (string.Equals(_lastTalkLine, s, StringComparison.Ordinal)) return;
			_lastTalkLine = s;

			Main.Instance?.SendToTolk(s);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: TalkPanelCtrl_ShowWaitInputIcon_Postfix failed: " + ex);
		}
	}

	private static void TutorialUI_ShowLeftCount_Postfix(object __instance, int count)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// 直接讀 UI 上的字串（已經套用本地化格式），避免自己硬拼語言。
			object tmp = __instance.GetType().GetField("_leftCountText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			string s = tmp?.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (string.IsNullOrWhiteSpace(s)) return;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			if (string.Equals(_lastTutorialLeftCount, s, StringComparison.Ordinal)) return;
			_lastTutorialLeftCount = s;

			Main.Instance?.SendToTolk(s);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: TutorialUI_ShowLeftCount_Postfix failed: " + ex);
		}
	}

	private static void ResultInitState_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// 依原始碼：ResultInitState.OnStateBegin 進入結果頁並呼叫 ResultView.SetUp；
			// 這個時間點 UI 常會同步初始化/重設導覽焦點，可能引發額外 selection 朗讀。
			// 因此在「明確進場點」做短暫抑制，避免結果總結之外再多送一條。
			Main.Instance?.SuppressSelectionFor(1200);
		}
		catch { }
	}

	private static void ResultCloseState_OnStateBegin_Postfix(object __instance)
	{
		try
		{
			// 離開結果流程後，清掉結果文字監聽的 context
			Main.Instance?.ClearResultContext();
		}
		catch { }
	}

	private static void TutorialUI_PlayTextAsync_Prefix(object __instance, object __0)
	{
		try
		{
			// 教學對話：改由 TalkPanelCtrl.ShowWaitInputIcon(true) 統一在「完整文字已落到 UI」時朗讀，
			// 避免某些情況先朗讀到不完整/未處理過的字串。
			return;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: TutorialUI_PlayTextAsync_Prefix failed: " + ex);
		}
	}

	private static void MornNovelUtil_DOTextAsync_Prefix(string __0, object __1, object[] __args)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// 依遊戲原始碼：TutorialUI 也用 DOTextAsync 驅動 TalkPanelCtrl 打字機，
			// 但教學朗讀的正確觸發點是 TutorialUI.PlayTextAsync（拿到完整字串），不是 DOTextAsync 全域入口。
			// 因此：callback target 若是 TalkPanelCtrl，這裡直接跳過。
			if (IsDelegateTargetTalkPanelCtrl(__1)) return;
			if (string.IsNullOrWhiteSpace(__0)) return;

			// 注意：在 NovelScene，名字文字往往比台詞晚 1~2 幀更新；
			// 因此這裡不直接朗讀，而是把「台詞 + callback/args」交給 mod 延後幾十毫秒組合成「名字：台詞」。
			string s = StripTmpRichText(__0).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			var now = DateTime.Now;
			// 維持原本的短去重：只防止同一個 hook 在極短時間內重入，不用它來解「唸兩次」
			if (string.Equals(_lastNovelLineRequested, s, StringComparison.Ordinal) && (now - _lastNovelLineRequestedAt).TotalMilliseconds < 150)
				return;
			_lastNovelLineRequested = s;
			_lastNovelLineRequestedAt = now;

			Main.Instance?.RequestSpeakNovelLine(s, __1, __args);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: MornNovelUtil_DOTextAsync_Prefix failed: " + ex);
		}
	}

	private static void ResultView_SetUp_Postfix(object __instance, object __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;
			// __0 = RuntimePlayData（依原始碼：ResultView.SetUp(RuntimePlayData runtimePlayData)）
			Main.Instance?.EnterResultContextFromView(__instance);

			Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");

			string ReadTextFromTmp(object tmp)
			{
				try
				{
					if (tmp == null) return null;
					var s = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
					s = StripTmpRichText(s).Trim();
					return string.IsNullOrWhiteSpace(s) ? null : s;
				}
				catch { return null; }
			}

			bool LooksLikeValue(string s)
			{
				if (string.IsNullOrWhiteSpace(s)) return false;
				s = s.Trim();
				if (s.Contains("%") || s.Contains("ms") || s.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || s.Contains(" x ")) return true;
				// 含任意數字也視為 value（例如 1234, 12/34 等）
				for (int i = 0; i < s.Length; i++)
				{
					if (char.IsDigit(s[i])) return true;
				}
				double _;
				return double.TryParse(s, out _);
			}

			object GetField(string fieldName)
			{
				try
				{
					var f = __instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
					return f?.GetValue(__instance);
				}
				catch { return null; }
			}

			string FindLabelNearTmp(object valueTmp, string valueText)
			{
				try
				{
					if (tmpType == null) return null;
					if (valueTmp == null || string.IsNullOrWhiteSpace(valueText)) return null;

					bool TryGetLocalXY(object tmp, out float x, out float y)
					{
						x = 0f; y = 0f;
						try
						{
							var go0 = tmp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tmp);
							var tr0 = go0?.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go0);
							var lp = tr0?.GetType().GetProperty("localPosition", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr0);
							if (lp == null) return false;
							// Vector3 x/y (field or property)
							var fx = lp.GetType().GetField("x");
							var fy = lp.GetType().GetField("y");
							if (fx != null && fy != null)
							{
								x = Convert.ToSingle(fx.GetValue(lp));
								y = Convert.ToSingle(fy.GetValue(lp));
								return true;
							}
							var px = lp.GetType().GetProperty("x", BindingFlags.Instance | BindingFlags.Public);
							var py = lp.GetType().GetProperty("y", BindingFlags.Instance | BindingFlags.Public);
							if (px != null && py != null)
							{
								x = Convert.ToSingle(px.GetValue(lp));
								y = Convert.ToSingle(py.GetValue(lp));
								return true;
							}
						}
						catch { }
						return false;
					}

					float vx, vy;
					bool hasV = TryGetLocalXY(valueTmp, out vx, out vy);

					// valueTmp.gameObject.transform.parent（有些 prefab label 不在同一層，最多往上兩層找）
					var go = valueTmp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(valueTmp);
					var tr = go?.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
					string best = null;
					double bestScore = double.MaxValue;
					for (int depth = 0; depth < 2; depth++)
					{
						var parentTr = tr?.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
						var parentGo = parentTr?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parentTr);
						if (parentGo == null) break;

						// GameObject.GetComponentsInChildren(Type, bool)
						var mi = parentGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
						var arr = mi?.Invoke(parentGo, new object[] { tmpType, true }) as Array;
						if (arr != null)
						{
							for (int i = 0; i < arr.Length; i++)
							{
								var c = arr.GetValue(i);
								if (c == null) continue;
								var s = ReadTextFromTmp(c);
								if (string.IsNullOrWhiteSpace(s)) continue;
								if (string.Equals(s, valueText, StringComparison.Ordinal)) continue;
								if (LooksLikeValue(s)) continue;
								if (s.Length > 24) continue;

								// 依 UI 位置配對：通常 label 在 value 左邊、y 接近
								double score;
								if (hasV)
								{
									float lx, ly;
									if (TryGetLocalXY(c, out lx, out ly))
									{
										// label 應該在左側；右側的通常是別的 value
										if (lx > vx + 0.01f) continue;
										var dy = Math.Abs((double)(ly - vy));
										var dx = (double)(vx - lx);
										score = dy * 1000.0 + dx; // 先比 y，再比距離
									}
									else
									{
										// 沒位置就退回用長度
										score = 1000000.0 + s.Length;
									}
								}
								else
								{
									// 沒位置就退回用長度
									score = 1000000.0 + s.Length;
								}

								if (best == null || score < bestScore)
								{
									best = s;
									bestScore = score;
								}
							}
						}

						// 往上一層
						tr = parentTr;
					}
					return string.IsNullOrWhiteSpace(best) ? null : best;
				}
				catch { return null; }
			}

			string ReadLabeled(string fieldName)
			{
				try
				{
					var tmp = GetField(fieldName);
					var v = ReadTextFromTmp(tmp);
					if (string.IsNullOrWhiteSpace(v)) return null;
					var label = FindLabelNearTmp(tmp, v);
					return string.IsNullOrWhiteSpace(label) ? v : (label + " " + v);
				}
				catch { return null; }
			}

			// 依遊戲原始碼：ResultView.SetUp 會設定這些欄位（我們盡量把 UI 上的 label 一起讀出來）
			var areaNum = ReadTextFromTmp(GetField("_areaNumLabel"));
			var area = ReadTextFromTmp(GetField("_areaLabel"));
			var game = ReadTextFromTmp(GetField("_gameLabel"));

			// 若 UI 文字沒有取到（例如某些結算流程/場景變體），用原始碼同一路徑從 runtimePlayData.GameDetail 取回
			string difficultyStars = null;
			try
			{
				object rpd = __0;
				object gd = null;
				try { gd = rpd?.GetType().GetField("GameDetail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(rpd); } catch { gd = null; }
				if (gd == null)
					try { gd = rpd?.GetType().GetProperty("GameDetail", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(rpd); } catch { gd = null; }

				string lang = null;
				try
				{
					var loc = __instance.GetType().GetField("_localizeCore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
					lang = loc?.GetType().GetProperty("CurrentLanguage", BindingFlags.Instance | BindingFlags.Public)?.GetValue(loc) as string;
				}
				catch { lang = null; }

				if (gd != null)
				{
					if (string.IsNullOrWhiteSpace(areaNum))
					{
						try
						{
							var f = gd.GetType().GetField("AreaNumName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var v = f?.GetValue(gd) as string;
							v = StripTmpRichText(v ?? "").Trim();
							if (!string.IsNullOrWhiteSpace(v)) areaNum = v;
						}
						catch { }
					}
					if (string.IsNullOrWhiteSpace(area))
					{
						try
						{
							var mi = gd.GetType().GetMethod("GameName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var v = (mi != null && !string.IsNullOrWhiteSpace(lang)) ? (mi.Invoke(gd, new object[] { lang }) as string) : null;
							v = StripTmpRichText(v ?? "").Trim();
							if (!string.IsNullOrWhiteSpace(v)) area = v;
						}
						catch { }
					}
					if (string.IsNullOrWhiteSpace(game))
					{
						try
						{
							var mi = gd.GetType().GetMethod("MusicName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							var v = (mi != null && !string.IsNullOrWhiteSpace(lang)) ? (mi.Invoke(gd, new object[] { lang }) as string) : null;
							v = StripTmpRichText(v ?? "").Trim();
							if (!string.IsNullOrWhiteSpace(v)) game = v;
						}
						catch { }
					}

					try
					{
						var mi = gd.GetType().GetMethod("GetDifficulty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						var v = mi?.Invoke(gd, null) as string;
						v = StripTmpRichText(v ?? "").Trim();
						if (!string.IsNullOrWhiteSpace(v)) difficultyStars = v;
					}
					catch { difficultyStars = null; }
				}
			}
			catch { difficultyStars = null; }

			// Result 畫面常有一個「標題」（可能是本地化靜態文字，不一定由 ResultView.SetUp 寫入）。
			// 這裡直接從 Result 的 Canvas root 掃描 TMP_Text，挑出最像標題的那一個（不硬寫字串）。
			string header = null;
			try
			{
				if (tmpType != null)
				{
					bool IsSelectableUnder(object go0)
					{
						try
						{
							if (go0 == null) return false;
							Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI") ?? Type.GetType("UnityEngine.UI.Selectable");
							if (selectableType == null) return false;
							var tr0 = go0.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go0);
							int guard = 0;
							while (tr0 != null && guard++ < 24)
							{
								var curGo = tr0.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr0);
								var has = curGo?.GetType().GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(curGo, new object[] { selectableType });
								if (has != null) return true;
								tr0 = tr0.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr0);
							}
						}
						catch { }
						return false;
					}

					bool TryGetLocalY(object go0, out float y)
					{
						y = 0f;
						try
						{
							var tr0 = go0?.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go0);
							var lp = tr0?.GetType().GetProperty("localPosition", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr0);
							if (lp == null) return false;
							var fy = lp.GetType().GetField("y");
							if (fy != null) { y = Convert.ToSingle(fy.GetValue(lp)); return true; }
							var py = lp.GetType().GetProperty("y", BindingFlags.Instance | BindingFlags.Public);
							if (py != null) { y = Convert.ToSingle(py.GetValue(lp)); return true; }
						}
						catch { }
						return false;
					}

					bool TryGetFontSize(object tmp, out float fs)
					{
						fs = 0f;
						try
						{
							var p = tmp?.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
							if (p == null) return false;
							var v = p.GetValue(tmp);
							fs = Convert.ToSingle(v);
							return true;
						}
						catch { return false; }
					}

					// 找 Canvas root
					object rootGo = null;
					try
					{
						Type canvasType = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule")
						                 ?? Type.GetType("UnityEngine.Canvas, UnityEngine.UI")
						                 ?? Type.GetType("UnityEngine.Canvas");
						var myGo = __instance.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(__instance);
						var tr0 = myGo?.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(myGo);
						while (tr0 != null)
						{
							var go0 = tr0.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr0);
							if (go0 == null) break;
							var hasCanvas = (canvasType == null) ? null : go0.GetType().GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(go0, new object[] { canvasType });
							if (hasCanvas != null) { rootGo = go0; break; }
							tr0 = tr0.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr0);
						}
						if (rootGo == null) rootGo = myGo;
					}
					catch { rootGo = null; }

					var comp = rootGo?.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rootGo);
					var mi = comp?.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
					var arr = mi?.Invoke(comp, new object[] { tmpType, true }) as Array;
					if (arr != null)
					{
						string best = null;
						double bestScore = double.MinValue;
						for (int i = 0; i < arr.Length; i++)
						{
							var tmp = arr.GetValue(i);
							if (tmp == null) continue;
							var s = ReadTextFromTmp(tmp);
							if (string.IsNullOrWhiteSpace(s)) continue;
							if (s.Length > 24) continue;
							if (LooksLikeValue(s)) continue;
							if (string.Equals(s, areaNum, StringComparison.Ordinal)) continue;
							if (string.Equals(s, area, StringComparison.Ordinal)) continue;
							if (string.Equals(s, game, StringComparison.Ordinal)) continue;

							// 不要把按鈕文字當標題
							var go0 = tmp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tmp);
							if (IsSelectableUnder(go0)) continue;

							float y = 0f; TryGetLocalY(go0, out y);
							float fs = 0f; TryGetFontSize(tmp, out fs);
							// score: 先看 fontSize，再看 y（越上面越像標題）
							double score = fs * 1000.0 + y;
							if (best == null || score > bestScore)
							{
								best = s;
								bestScore = score;
							}
						}

						if (!string.IsNullOrWhiteSpace(best))
						{
							var now = DateTime.Now;
							if (!(string.Equals(_lastResultHeader, best, StringComparison.Ordinal) && (now - _lastResultHeaderAt).TotalMilliseconds < 1500))
							{
								_lastResultHeader = best;
								_lastResultHeaderAt = now;
								header = best;
							}
						}
					}
				}
			}
			catch { header = null; }

			// clear rate 本身就是 UI 上的完整文字（例如 "95.0%"），不要再去找 label，避免把「完美!」這種結果大字錯配過來
			var clear = ReadTextFromTmp(GetField("_clearRate"));
			var perfect = ReadLabeled("_perfectCount");
			var fast = ReadLabeled("_fastCount");
			var late = ReadLabeled("_lateCount");
			var miss = ReadLabeled("_missCount");

			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(header)) parts.Add(header);
			if (!string.IsNullOrWhiteSpace(areaNum)) parts.Add(areaNum);
			if (!string.IsNullOrWhiteSpace(area)) parts.Add(area);
			if (!string.IsNullOrWhiteSpace(game)) parts.Add(game);
			if (!string.IsNullOrWhiteSpace(difficultyStars)) parts.Add(difficultyStars);
			if (!string.IsNullOrWhiteSpace(clear)) parts.Add(clear);

			// 盡量用 UI 上的 label + value（不硬寫翻譯）
			if (!string.IsNullOrWhiteSpace(perfect)) parts.Add(perfect);
			if (!string.IsNullOrWhiteSpace(fast)) parts.Add(fast);
			if (!string.IsNullOrWhiteSpace(late)) parts.Add(late);
			if (!string.IsNullOrWhiteSpace(miss)) parts.Add(miss);

			var spoken = string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
			if (string.IsNullOrWhiteSpace(spoken)) return;

			Main.Instance?.SendToTolk(spoken);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: ResultView_SetUp_Postfix failed: " + ex);
		}
	}

	private static void TMPText_set_text_ResultOnly_Postfix(object __instance, string __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;

			// 校正/設定數值：只針對我們註冊過的 TMP_Text instanceId 做朗讀（不會變成全域洗語音）
			try
			{
				int id = GetUnityInstanceId(__instance);
				// Timing hint capture: 某些版本提示文字「不會 OnEnable」，但會走 TMP_Text.set_text。
				// 因此在 capture window 內，允許在 set_text 時把該 TMP 註冊並以「對話框正文」方式唸一次。
				try
				{
					if (id != 0 && _timingHintCaptureRootGo != null && DateTime.Now <= _timingHintCaptureUntil)
					{
						var go = __instance?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(__instance);
						if (go != null && IsSameOrDescendant(go, _timingHintCaptureRootGo, 40))
						{
							string s0 = StripTmpRichText(__0 ?? "").Trim();
							// exclude the value text itself (ms)
							if (!string.IsNullOrWhiteSpace(s0) && s0.IndexOf("ms", StringComparison.OrdinalIgnoreCase) < 0 && s0.Length >= 4)
							{
								_timingTmpTextIds.Add(id);
								var now0 = DateTime.Now;
								if (!(string.Equals(_lastTimingHintText, s0, StringComparison.Ordinal) && (now0 - _lastTimingHintAt).TotalMilliseconds < 1500))
								{
									_lastTimingHintText = s0;
									_lastTimingHintAt = now0;
									Main.Instance?.SpeakDialogBodyOnceDelayed(s0, 1400);
								}
								_timingHintCaptureUntil = DateTime.MinValue;
							}
						}
					}
				}
				catch { }

				if (id != 0 && _timingTmpTextIds.Contains(id))
				{
					string s = StripTmpRichText(__0 ?? "").Trim();
					var now = DateTime.Now;
					// Hint text: route through dialog-like output (once)
					if (!string.IsNullOrWhiteSpace(s) && s.IndexOf("ms", StringComparison.OrdinalIgnoreCase) < 0 && s.Length >= 4)
					{
						if (!(string.Equals(_lastTimingHintText, s, StringComparison.Ordinal) && (now - _lastTimingHintAt).TotalMilliseconds < 1500))
						{
							_lastTimingHintText = s;
							_lastTimingHintAt = now;
							Main.Instance?.SpeakDialogBodyOnceDelayed(s, 1400);
						}
					}
					// Value text (ms): normal timing value speak
					else if (!string.IsNullOrWhiteSpace(s) &&
					         !(string.Equals(_lastTimingSettingText, s, StringComparison.Ordinal) && (now - _lastTimingSettingTextAt).TotalMilliseconds < 180))
					{
						_lastTimingSettingText = s;
						_lastTimingSettingTextAt = now;
						Main.Instance?.SendToTolk(s);
					}
				}
				else if (id != 0 && _settingsTmpTextIds.Contains(id))
				{
					string s = StripTmpRichText(__0 ?? "").Trim();
					var now = DateTime.Now;
					if (!string.IsNullOrWhiteSpace(s) &&
					    !(string.Equals(_lastSettingsValueText, s, StringComparison.Ordinal) && (now - _lastSettingsValueTextAt).TotalMilliseconds < 180))
					{
						_lastSettingsValueText = s;
						_lastSettingsValueTextAt = now;
						Main.Instance?.SendToTolk(s);
					}
				}
				else if (id != 0 && _startupTmpTextIds.Contains(id))
				{
					string s = StripTmpRichText(__0 ?? "").Trim();
					var now = DateTime.Now;
					if (!string.IsNullOrWhiteSpace(s) &&
					    !(string.Equals(_lastStartupSpoken, s, StringComparison.Ordinal) && (now - _lastStartupSpokenAt).TotalMilliseconds < 300))
					{
						_lastStartupSpoken = s;
						_lastStartupSpokenAt = now;
						Main.Instance?.SendToTolk(s);
					}
				}
				else if (id != 0 && _loadingTmpTextIds.Contains(id))
				{
					string s = StripTmpRichText(__0 ?? "").Trim();
					var now = DateTime.Now;
					if (!string.IsNullOrWhiteSpace(s) &&
					    !(string.Equals(_lastLoadingSpoken, s, StringComparison.Ordinal) && (now - _lastLoadingSpokenAt).TotalMilliseconds < 300))
					{
						_lastLoadingSpoken = s;
						_lastLoadingSpokenAt = now;
						Main.Instance?.SendToTolk(s);
					}
				}
			}
			catch { }

			Main.Instance?.NotifyResultTextChanged(__instance, __0);
			// 設定分類標題（Language / Other 等）：只在進入設定頁的短窗口內收集候選，不會全域洗語音。
			Main.Instance?.NotifySettingsCategoryTitleTextChanged(__instance, __0);
		}
		catch { }
	}

	private static void UnityUIText_set_text_ResultOnly_Postfix(object __instance, string __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.NotifyResultTextChanged(__instance, __0);
			Main.Instance?.NotifySettingsCategoryTitleTextChanged(__instance, __0);
		}
		catch { }
	}

	private static void TimingText_Set_Postfix(object __instance, object beatEvaluation)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			if (__instance == null) return;

			// 直接讀 UI 上的字串（已經是遊戲實際顯示的內容），避免自己計算數值或翻譯。
			object tmp = __instance.GetType().GetField("_diffText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance);
			if (tmp == null) return;

			// 只在它「顯示中」才唸（避免 NoInput/Free 等狀態也觸發）
			if (!IsComponentActive(tmp)) return;

			string s = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
			if (string.IsNullOrWhiteSpace(s)) return;
			s = StripTmpRichText(s).Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			// 節流/去重：此文字可能高頻觸發，避免洗爆語音
			var now = DateTime.Now;
			if (string.Equals(_lastTimingDiffText, s, StringComparison.Ordinal) && (now - _lastTimingDiffAt).TotalMilliseconds < 400)
				return;
			_lastTimingDiffText = s;
			_lastTimingDiffAt = now;

			Main.Instance?.SendToTolk(s);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: TimingText_Set_Postfix failed: " + ex);
		}
	}

	private static void ParadeCredit_InitializeAsync_Prefix(object __instance, object creditInfo, object ct)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			Main.Instance?.ScheduleParadeCreditSpeak(__instance, creditInfo);
		}
		catch { }
	}

	private static void EventSystem_SetSelectedGameObject_Postfix(object __instance, object __0)
	{
		try
		{
			if (!AutoSpeakEnabled) return;
			// 用 __0 當作「目標焦點」，避免切頁/切語言時先唸上一個 selection 一次
			Main.Instance?.TriggerReadSelection(__0);
			// Prefab/Localize 靜態文字：在畫面啟用窗口內，利用 selection 變更時機再掃一次 root（避免只有早期掃描、錯過最終文字）
			if (_startupCaptureRootGo != null && DateTime.Now <= _startupCaptureUntil)
				RegisterHeuristicTmpTextIdsUnderRoot(_startupCaptureRootGo, _startupTmpTextIds, ref _startupLastHeuristicScanAt);
			if (_loadingCaptureRootGo != null && DateTime.Now <= _loadingCaptureUntil)
				RegisterHeuristicTmpTextIdsUnderRoot(_loadingCaptureRootGo, _loadingTmpTextIds, ref _loadingLastHeuristicScanAt);
			if (_settingsCaptureRootGo != null && DateTime.Now <= _settingsCaptureUntil)
				RegisterHeuristicTmpTextIdsUnderRoot(_settingsCaptureRootGo, _settingsTmpTextIds, ref _settingsLastHeuristicScanAt);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: EventSystem postfix failed: " + ex);
		}
	}

	private static bool IsComponentActive(object component)
	{
		try
		{
			// enabled
			var pEnabled = component.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
			if (pEnabled != null)
			{
				var enObj = pEnabled.GetValue(component);
				if (enObj is bool en && !en) return false;
			}

			// gameObject.activeInHierarchy / activeSelf
			var go = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
			if (go != null)
			{
				var pAih = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public);
				if (pAih != null)
				{
					var v = pAih.GetValue(go);
					if (v is bool b) return b;
				}
				var pAs = go.GetType().GetProperty("activeSelf", BindingFlags.Instance | BindingFlags.Public);
				if (pAs != null)
				{
					var v = pAs.GetValue(go);
					if (v is bool b) return b;
				}
			}
		}
		catch { }
		return true;
	}
}
