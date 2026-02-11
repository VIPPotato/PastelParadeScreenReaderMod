using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MelonLoader;
using HarmonyLib;

namespace PastelParadeAccess;

public partial class Main : MelonMod
{
	private delegate int TolkOutputDelegate([MarshalAs(UnmanagedType.LPWStr)] string text, [MarshalAs(UnmanagedType.I1)] bool interrupt);
	private delegate void TolkLoadDelegate();
	private delegate void TolkUnloadDelegate();

	private bool tolkInitialized;
	private volatile bool _isQuitting;
	private object _sapiVoice;
	private bool _sapiFailed;

	private DateTime _suppressTextSendsUntil = DateTime.MinValue;
	private DateTime _lastSelectionReadAt = DateTime.MinValue;

	// DebugPanel common text stream -> phase-based speech
	private enum DebugFlowPhase
	{
		Startup,
		Loading,
		Gameplay
	}

	private DebugFlowPhase _debugPhase = DebugFlowPhase.Startup;
	private DateTime _debugStartupUntil = DateTime.MinValue;
	private DateTime _debugLoadingUntil = DateTime.MinValue;
	private DateTime _debugLastLoadingEnteredAt = DateTime.MinValue;
	private readonly List<string> _debugStartupLines = new List<string>(64);
	private readonly List<string> _debugLoadingLines = new List<string>(64);
	private string _lastDebugStartupSpoken;
	private string _lastDebugLoadingSpoken;

	// Delayed TMP speech (e.g., calibration hint): wait 1-2s for final text
	private readonly List<(DateTime at, string text)> _delayedDialogBodies = new List<(DateTime at, string text)>(8);

	// Result（評價畫面）期間：用來「只在結果 UI 範圍內」抓文字更新（模仿 patch2 的成功點，但避免全域 set_text 洗語音）
	private int _resultRootInstanceId;
	private DateTime _resultContextUntil = DateTime.MinValue;
	private readonly HashSet<int> _resultKnownValueTextIds = new HashSet<int>();

	private IntPtr nativeLibraryHandle = IntPtr.Zero;

	private string _lastSentText;
	private DateTime _lastSentAt = DateTime.MinValue;
	private int _dialogRootInstanceId;
	private string _dialogBundleText;
	private DateTime _dialogBundleSpokenAt = DateTime.MinValue;
	private DateTime _dialogContextUntil = DateTime.MinValue;
	private string _lastLoggedSpeak;
	private DateTime _lastLoggedSpeakAt = DateTime.MinValue;
	private string _lastSpokenCategoryTitle;
	private DateTime _lastSpokenCategoryAt = DateTime.MinValue;
	private bool _pendingCategoryTitleSpeak;
	private DateTime _pendingCategoryTitleSpeakAt = DateTime.MinValue;
	private int _pendingCategoryTries;
	private DateTime _pendingCategoryDeadline = DateTime.MinValue;
	private object _pendingCategoryContext;
	private string _pendingCategoryLastCandidate;
	private int _pendingCategoryStableCount;
	// 設定分類標題：若標題在「另一個 Canvas / 外層 Header」且由 UI 系統寫入 text，我們用 set_text hook 捕捉最佳候選
	private string _pendingCategoryCapturedBest;
	private float _pendingCategoryCapturedBestScore = float.NegativeInfinity;
	private string _pendingCategoryCapturedBestNonSelectable;
	private float _pendingCategoryCapturedBestNonSelectableScore = float.NegativeInfinity;
	private DateTime _ignoreSliderChangesUntil = DateTime.MinValue;
	private object _pendingSliderGo;
	private DateTime _pendingSliderSpeakAt = DateTime.MinValue;
	private object _pendingToggleGo;
	private DateTime _pendingToggleSpeakAt = DateTime.MinValue;
	private string _lastSliderSpokenText;
	private DateTime _lastSliderSpokenAt = DateTime.MinValue;
	private static readonly Regex _richTextTagRegex = new Regex("<[^>]*>", RegexOptions.Compiled);
	// (removed) world-map move flag: bump feature removed

	private string _pendingNovelLine;
	private object _pendingNovelCallback;
	private object[] _pendingNovelArgs;
	private DateTime _pendingNovelSpeakAt = DateTime.MinValue;
	private DateTime _pendingNovelDeadline = DateTime.MinValue;

	// Ending（Parade）字幕：用 ParadeCredit.InitializeAsync 的 delay 排程一次朗讀（避免全域 set_text hook）
	private string _pendingParadeCreditText;
	private DateTime _pendingParadeCreditSpeakAt = DateTime.MinValue;

	// 翻頁：合併「tab 標題 + 焦點項目」一次輸出
	private string _pendingTabTitle;
	private string _pendingTabFocus;
	private DateTime _pendingTabSpeakAt = DateTime.MinValue;
	private DateTime _pendingTabDeadline = DateTime.MinValue;
	private DateTime _suppressSelectionUntil = DateTime.MinValue;

	private readonly ConcurrentQueue<string> _speechQueue = new ConcurrentQueue<string>();
	private readonly AutoResetEvent _speechEvent = new AutoResetEvent(false);
	private DateTime _lastTolkOutputFailAt = DateTime.MinValue;
	private string _lastTolkOutputFailText;
	private Task _speechWorker;
	private int _speechQueueCount;

	// 依遊戲原始碼（`Pastel☆Parade/UIDisplaySettings.cs`、`UISoundSettingsState.cs`）：
	// 真正的「開關」是 MornUGUIButton.AsToggle（全螢幕/VSync/抗鋸齒/震動/判定詳情）。
	// 但很多 UI（含主選單/清單）也可能用 AsToggle 來做選取高亮；那不該唸 on/off。
	// 因此改成白名單：只有我們從設定 State instance 取到的那些 toggle 按鈕才允許輸出 on/off。
	private readonly HashSet<int> _knownSettingsToggleIds = new HashSet<int>();

	// 只有「真的有開關的設定頁」才允許 0/1 fallback（音訊/顯示）；校準介面不該出現 0/1
	// 先前用來限制 0/1 fallback 的旗標，已改成用「元件本身是否為 toggle」決定，保留會造成誤用與警告

	private bool _debugModeEnabled;
	private bool _menuPositionAnnouncementsEnabled;

	public static Main Instance { get; private set; }

	private string outDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tolk");

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetDllDirectoryW(string lpPathName);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryW(string lpFileName);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FreeLibrary(IntPtr hModule);

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_Load")]
	private static extern void Tolk_Load();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_Unload")]
	private static extern void Tolk_Unload();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_Output")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool Tolk_Output([MarshalAs(UnmanagedType.LPWStr)] string text, [MarshalAs(UnmanagedType.I1)] bool interrupt);

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_HasSpeech")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool Tolk_HasSpeech();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_TrySAPI")]
	private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.I1)] bool trySapi);

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_PreferSAPI")]
	private static extern void Tolk_PreferSAPI([MarshalAs(UnmanagedType.I1)] bool preferSapi);

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Tolk_DetectScreenReader")]
	private static extern IntPtr Tolk_DetectScreenReader();

	[Obsolete]
	public override void OnApplicationStart()
	{
		Instance = this;
		Directory.CreateDirectory(outDir);
		Loc.Initialize();
		ModConfig.Initialize();
		_debugModeEnabled = ModConfig.DebugModeEnabled;
		_menuPositionAnnouncementsEnabled = ModConfig.MenuPositionAnnouncementsEnabled;
		DebugLogger.SetEnabled(_debugModeEnabled);

		// Debug flow: startup window
		try
		{
			_debugPhase = DebugFlowPhase.Startup;
			_debugStartupUntil = DateTime.Now.AddMilliseconds(1500);
			_debugLoadingUntil = DateTime.MinValue;
			_debugStartupLines.Clear();
			_debugLoadingLines.Clear();
			_lastDebugStartupSpoken = null;
			_lastDebugLoadingSpoken = null;
		}
		catch { }

		// 重要：語音輸出可能會阻塞主執行緒（造成 UI 選取延遲），改成背景佇列輸出。
		try
		{
			_speechWorker = Task.Run(SpeechWorkerLoop);
		}
		catch { }

		// 先掛上「很早就會出現」的 patch（例如地圖名稱），避免因為延遲/耗時 patch 流程漏掉第一次顯示。
		try
		{
			Patches.ApplyEarlyPatches();
		}
		catch { }

		try
		{
			AppDomain.CurrentDomain.ProcessExit += (_, __) => BeginQuit("ProcessExit");
			AppDomain.CurrentDomain.DomainUnload += (_, __) => BeginQuit("DomainUnload");
		}
		catch { }

		Task.Run(async delegate
		{
			// Init/完整 patch 仍放背景，避免卡住遊戲啟動；但不再固定等 2 秒。
			await Task.Delay(200);
			try { InitTolk(); } catch { }
			try { SendToTolkPriority(Loc.Get("mod_loaded")); } catch { }
			try { Patches.ApplyPatches(); }
			catch (Exception ex) { MelonLogger.Error("TolkExporter: ApplyPatches failed: " + ex); }
		});
	}

	public override void OnUpdate()
	{
		try
		{
			if (_isQuitting) return;
			ProcessRuntimeHotkeys();

			FlushDelayedDialogBodies();

			// Debug flow phase machine output
			TryFlushDebugFlowIfNeeded();

			// Ending（Parade）字幕：在預定時間點唸一次
			if (!string.IsNullOrWhiteSpace(_pendingParadeCreditText) && DateTime.Now >= _pendingParadeCreditSpeakAt)
			{
				var s = _pendingParadeCreditText;
				_pendingParadeCreditText = null;
				_pendingParadeCreditSpeakAt = DateTime.MinValue;
				SendToTolk(s);
			}

			// 劇情/小說：名字可能晚一點才更新，延後 1~2 幀再組合「名字：台詞」
			if (!string.IsNullOrWhiteSpace(_pendingNovelLine) && DateTime.Now >= _pendingNovelSpeakAt)
			{
				var now = DateTime.Now;
				var line = _pendingNovelLine;
				var cb = _pendingNovelCallback;
				var args = _pendingNovelArgs;

				string speaker = TryFindNovelSpeaker(line, cb, args);
				if (!string.IsNullOrWhiteSpace(speaker) || (_pendingNovelDeadline != DateTime.MinValue && now >= _pendingNovelDeadline))
				{
					_pendingNovelLine = null;
					_pendingNovelCallback = null;
					_pendingNovelArgs = null;
					_pendingNovelSpeakAt = DateTime.MinValue;
					_pendingNovelDeadline = DateTime.MinValue;

					string spoken = (!string.IsNullOrWhiteSpace(speaker) && !string.Equals(speaker, line, StringComparison.Ordinal))
						? (speaker + "：" + line)
						: line;
					SendToTolk(spoken);
				}
				else
				{
					_pendingNovelSpeakAt = now.AddMilliseconds(20);
				}
			}

			// 設定分類標題：等 UI 文字「穩定」後再唸（避免唸到上一個分類）
			if (_pendingCategoryTitleSpeak && DateTime.Now >= _pendingCategoryTitleSpeakAt)
			{
				var now = DateTime.Now;
				string candidate = null;
				try
				{
					var tn = _pendingCategoryContext?.GetType().FullName ?? "";
					// 語言頁（UILocalizeState）常是外層 Header 更新標題，優先用 set_text 捕捉到的候選
					if (tn == "PastelParade.UILocalizeState")
						candidate = _pendingCategoryCapturedBestNonSelectable ?? _pendingCategoryCapturedBest ?? GetSettingsCategoryTitleCandidate(_pendingCategoryContext);
					else
						candidate = GetSettingsCategoryTitleCandidate(_pendingCategoryContext) ?? _pendingCategoryCapturedBestNonSelectable ?? _pendingCategoryCapturedBest;
				}
				catch
				{
					// fallback 時也要保留已捕捉到的候選，避免例外導致「deadline 到了也 0 次朗讀」
					candidate = _pendingCategoryCapturedBestNonSelectable ?? _pendingCategoryCapturedBest ?? GetSettingsCategoryTitleCandidate(_pendingCategoryContext);
				}
				if (!string.IsNullOrWhiteSpace(candidate))
				{
					if (string.Equals(_pendingCategoryLastCandidate, candidate, StringComparison.Ordinal))
						_pendingCategoryStableCount++;
					else
					{
						_pendingCategoryLastCandidate = candidate;
						_pendingCategoryStableCount = 1;
					}

					// 連續兩次一樣才唸；但若已經到 deadline，就直接唸目前候選（避免完全不唸）
					if (_pendingCategoryStableCount >= 2 || (_pendingCategoryDeadline != DateTime.MinValue && now >= _pendingCategoryDeadline))
					{
						// 進頁可能會觸發多次，做最小去重
						if (!(string.Equals(_lastSpokenCategoryTitle, candidate, StringComparison.Ordinal) && (now - _lastSpokenCategoryAt).TotalMilliseconds < 500))
						{
							_lastSpokenCategoryTitle = candidate;
							_lastSpokenCategoryAt = now;
							// 不插隊：交給使用者/讀屏軟體決定是否中斷
							SendToTolk(candidate);
						}

						_pendingCategoryTitleSpeak = false;
						_pendingCategoryTitleSpeakAt = DateTime.MinValue;
						_pendingCategoryTries = 0;
						_pendingCategoryDeadline = DateTime.MinValue;
						_pendingCategoryContext = null;
						_pendingCategoryLastCandidate = null;
						_pendingCategoryStableCount = 0;
						_pendingCategoryCapturedBest = null;
						_pendingCategoryCapturedBestScore = float.NegativeInfinity;
						_pendingCategoryCapturedBestNonSelectable = null;
						_pendingCategoryCapturedBestNonSelectableScore = float.NegativeInfinity;
					}
					else
					{
						// 短重試：標題可能晚 1~2 幀才更新
						_pendingCategoryTitleSpeakAt = now.AddMilliseconds(20);
					}
				}
				else
				{
					_pendingCategoryTries++;
					if (_pendingCategoryDeadline != DateTime.MinValue && now >= _pendingCategoryDeadline)
					{
						// deadline 到了：就算沒抓到穩定 candidate，也要 best-effort 唸一次（Pastel☆Parade 設定 UI 常不會穩定到連續兩次）
						try
						{
							var best = _pendingCategoryCapturedBestNonSelectable ?? _pendingCategoryCapturedBest ?? _pendingCategoryLastCandidate;
							best = NormalizeOutputText(best);
							if (!string.IsNullOrWhiteSpace(best))
							{
								if (!(string.Equals(_lastSpokenCategoryTitle, best, StringComparison.Ordinal) && (now - _lastSpokenCategoryAt).TotalMilliseconds < 800))
								{
									_lastSpokenCategoryTitle = best;
									_lastSpokenCategoryAt = now;
									SendToTolkPriority(best);
								}
							}
						}
						catch { }

						_pendingCategoryTitleSpeak = false;
						_pendingCategoryTitleSpeakAt = DateTime.MinValue;
						_pendingCategoryTries = 0;
						_pendingCategoryDeadline = DateTime.MinValue;
						_pendingCategoryContext = null;
						_pendingCategoryLastCandidate = null;
						_pendingCategoryStableCount = 0;
						_pendingCategoryCapturedBest = null;
						_pendingCategoryCapturedBestScore = float.NegativeInfinity;
						_pendingCategoryCapturedBestNonSelectable = null;
						_pendingCategoryCapturedBestNonSelectableScore = float.NegativeInfinity;
					}
					else
					{
						_pendingCategoryTitleSpeakAt = now.AddMilliseconds(20);
					}
				}
			}

			// Slider：把朗讀延到下一個 update，確保旁邊的 TextMeshProUGUI 已更新（避免唸舊值）
			if (_pendingSliderGo != null && DateTime.Now >= _pendingSliderSpeakAt)
			{
				var now = DateTime.Now;
				_pendingSliderSpeakAt = DateTime.MinValue;
				var go = _pendingSliderGo;
				_pendingSliderGo = null;

				// 節流：拖曳時避免太密（但不能到 1 秒那麼慢）
				if ((now - _lastSliderSpokenAt).TotalMilliseconds < 70) return;
				ReadSelectionAndSpeak(go);
				if (!string.IsNullOrWhiteSpace(_lastSentText) && !string.Equals(_lastSliderSpokenText, _lastSentText, StringComparison.Ordinal))
				{
					_lastSliderSpokenText = _lastSentText;
					_lastSliderSpokenAt = now;
				}
			}

			// Toggle：切換狀態時也要即時唸
			if (_pendingToggleGo != null && DateTime.Now >= _pendingToggleSpeakAt)
			{
				var go = _pendingToggleGo;
				_pendingToggleGo = null;
				_pendingToggleSpeakAt = DateTime.MinValue;
				ReadSelectionAndSpeak(go);
			}

			// 翻頁：等 UI 更新完後，把「tab 標題 + 焦點項目」合併唸一次
			if (!string.IsNullOrWhiteSpace(_pendingTabTitle) && DateTime.Now >= _pendingTabSpeakAt)
			{
				var now = DateTime.Now;
				var title = _pendingTabTitle;
				var focus = _pendingTabFocus;
				if (string.IsNullOrWhiteSpace(focus))
				{
					try { focus = Patches.TryBuildFocusTextFromSelection(); } catch { focus = null; }
				}

				if (!string.IsNullOrWhiteSpace(focus))
				{
					_pendingTabTitle = null;
					_pendingTabFocus = null;
					_pendingTabSpeakAt = DateTime.MinValue;
					_pendingTabDeadline = DateTime.MinValue;
					_suppressSelectionUntil = DateTime.MinValue;
					SendToTolk(title + " " + focus);
				}
				else if (_pendingTabDeadline != DateTime.MinValue && now >= _pendingTabDeadline)
				{
					_pendingTabTitle = null;
					_pendingTabFocus = null;
					_pendingTabSpeakAt = DateTime.MinValue;
					_pendingTabDeadline = DateTime.MinValue;
					_suppressSelectionUntil = DateTime.MinValue;
					SendToTolk(title);
				}
				else
				{
					_pendingTabSpeakAt = now.AddMilliseconds(25);
				}
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: OnUpdate exception: " + ex);
		}
	}

	private void ProcessRuntimeHotkeys()
	{
		try
		{
			if (IsHotkeyPressedThisFrame("F12", "f12Key"))
				ToggleDebugMode();

			if (IsHotkeyPressedThisFrame("F3", "f3Key"))
				ToggleMenuPositionAnnouncements();
		}
		catch
		{
		}
	}

	private void ToggleDebugMode()
	{
		try
		{
			_debugModeEnabled = !_debugModeEnabled;
			ModConfig.SetDebugMode(_debugModeEnabled);
			DebugLogger.SetEnabled(_debugModeEnabled);
			SendToTolkPriority(Loc.Get(_debugModeEnabled ? "debug_mode_on" : "debug_mode_off"));
		}
		catch
		{
		}
	}

	private void ToggleMenuPositionAnnouncements()
	{
		try
		{
			_menuPositionAnnouncementsEnabled = !_menuPositionAnnouncementsEnabled;
			ModConfig.SetMenuPositionAnnouncements(_menuPositionAnnouncementsEnabled);
			SendToTolkPriority(Loc.Get(_menuPositionAnnouncementsEnabled ? "menu_position_on" : "menu_position_off"));
		}
		catch
		{
		}
	}

	private static bool IsHotkeyPressedThisFrame(string keyCodeName, string keyboardKeyPropertyName)
	{
		return IsLegacyKeyDown(keyCodeName) || IsInputSystemKeyPressedThisFrame(keyboardKeyPropertyName);
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

	private void FlushDelayedDialogBodies()
	{
		try
		{
			if (_delayedDialogBodies.Count == 0) return;
			var now = DateTime.Now;
			for (int i = _delayedDialogBodies.Count - 1; i >= 0; i--)
			{
				var it = _delayedDialogBodies[i];
				if (now < it.at) continue;
				_delayedDialogBodies.RemoveAt(i);
				if (!string.IsNullOrWhiteSpace(it.text))
					SpeakDialogBodyOnce(it.text);
			}
		}
		catch { }
	}

	public override void OnSceneWasLoaded(int buildIndex, string sceneName)
	{
		try
		{
			if (_isQuitting) return;
			Loc.RefreshLanguage();
			EnterLoadingPhaseFromSceneEvent(sceneName);
		}
		catch { }
	}

	public override void OnSceneWasInitialized(int buildIndex, string sceneName)
	{
		try
		{
			if (_isQuitting) return;
			Loc.RefreshLanguage();
			EnterLoadingPhaseFromSceneEvent(sceneName);
		}
		catch { }
	}

	private void EnterLoadingPhaseFromSceneEvent(string sceneName)
	{
		try
		{
			// During startup collection, don't override phase; we want the initial summary.
			if (_debugPhase == DebugFlowPhase.Startup && DateTime.Now <= _debugStartupUntil) return;

			var now = DateTime.Now;
			// Throttle: multiple scene callbacks can fire for same transition
			if ((now - _debugLastLoadingEnteredAt).TotalMilliseconds < 400) return;
			_debugLastLoadingEnteredAt = now;

			_debugPhase = DebugFlowPhase.Loading;
			_debugLoadingUntil = now.AddMilliseconds(1700);
			_debugLoadingLines.Clear();
		}
		catch { }
	}

	internal void EnqueueDebugFlowText(string text)
	{
		try
		{
			if (_isQuitting) return;
			if (string.IsNullOrWhiteSpace(text)) return;

			text = NormalizeDebugFlowText(text);
			if (string.IsNullOrWhiteSpace(text)) return;
			if (IsDebugNoise(text)) return;

			var now = DateTime.Now;
			if (_debugPhase == DebugFlowPhase.Startup && now <= _debugStartupUntil)
			{
				if (_debugStartupLines.Count < 80) _debugStartupLines.Add(text);
			}
			else if (_debugPhase == DebugFlowPhase.Loading && now <= _debugLoadingUntil)
			{
				if (_debugLoadingLines.Count < 80) _debugLoadingLines.Add(text);
			}
			// Gameplay: do nothing
		}
		catch { }
	}

	private void TryFlushDebugFlowIfNeeded()
	{
		try
		{
			var now = DateTime.Now;
			if (_debugPhase == DebugFlowPhase.Startup && _debugStartupUntil != DateTime.MinValue && now >= _debugStartupUntil)
			{
				var summary = BuildStartupSummary(_debugStartupLines);
				_debugStartupLines.Clear();
				_debugStartupUntil = DateTime.MinValue;
				_debugPhase = DebugFlowPhase.Gameplay;

				if (!string.IsNullOrWhiteSpace(summary) && !string.Equals(_lastDebugStartupSpoken, summary, StringComparison.Ordinal))
				{
					_lastDebugStartupSpoken = summary;
					SendToTolkPriority(summary);
				}
			}

			if (_debugPhase == DebugFlowPhase.Loading && _debugLoadingUntil != DateTime.MinValue && now >= _debugLoadingUntil)
			{
				var tip = ChooseBestLoadingTip(_debugLoadingLines);
				_debugLoadingLines.Clear();
				_debugLoadingUntil = DateTime.MinValue;
				_debugPhase = DebugFlowPhase.Gameplay;

				if (!string.IsNullOrWhiteSpace(tip) && !string.Equals(_lastDebugLoadingSpoken, tip, StringComparison.Ordinal))
				{
					_lastDebugLoadingSpoken = tip;
					SendToTolkPriority(tip);
				}
			}
		}
		catch { }
	}

	private string NormalizeDebugFlowText(string s)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(s)) return null;
			s = _richTextTagRegex.Replace(s, "").Trim();
			s = NormalizeOutputText(s);
			return string.IsNullOrWhiteSpace(s) ? null : s;
		}
		catch { return null; }
	}

	private static bool IsDebugNoise(string s)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(s)) return true;
			var t = s.Trim();
			if (t.Length == 0) return true;

			// frame/fps spam
			if (t.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0) return true;
			if (t.IndexOf("fps", StringComparison.OrdinalIgnoreCase) >= 0) return true;

			// numeric-ish only
			bool allDigits = true;
			for (int i = 0; i < t.Length; i++)
			{
				char c = t[i];
				if (char.IsDigit(c) || c == '+' || c == '-' || c == '.' || c == '%' || c == '/' || c == ':' || c == ' ') continue;
				allDigits = false; break;
			}
			if (allDigits) return true;

			// beat debug like "0 - 1"
			if (t.Length <= 16)
			{
				int dash = t.IndexOf('-');
				if (dash >= 0)
				{
					string a = t.Substring(0, dash).Trim();
					string b = t.Substring(dash + 1).Trim();
					double da, db;
					if (double.TryParse(a, out da) && double.TryParse(b, out db)) return true;
				}
			}

			return false;
		}
		catch { return true; }
	}

	private static string BuildStartupSummary(List<string> lines)
	{
		try
		{
			if (lines == null || lines.Count == 0) return null;
			// de-dupe, keep order
			var uniq = new List<string>(lines.Count);
			for (int i = 0; i < lines.Count; i++)
			{
				var s = lines[i];
				if (string.IsNullOrWhiteSpace(s)) continue;
				bool dup = false;
				for (int j = 0; j < uniq.Count; j++)
					if (string.Equals(uniq[j], s, StringComparison.Ordinal)) { dup = true; break; }
				if (!dup) uniq.Add(s);
			}
			if (uniq.Count == 0) return null;

			// pick 1-3 best-scored strings (ensure game title isn't lost to long irrelevant logs)
			var scored = new List<(string s, double score)>(uniq.Count);
			for (int i = 0; i < uniq.Count; i++)
			{
				var s = (uniq[i] ?? "").Trim();
				if (s.Length < 2) continue;
				if (s.Length > 180) continue;
				if (s.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) continue;

				int digits = 0, letters = 0;
				for (int k = 0; k < s.Length; k++)
				{
					char c = s[k];
					if (char.IsDigit(c)) digits++;
					else if (char.IsLetter(c)) letters++;
				}
				double score = s.Length + letters * 2 - digits * 4;
				scored.Add((s, score));
			}
			if (scored.Count == 0) return null;
			scored.Sort((a, b) => b.score.CompareTo(a.score));

			var take = new List<string>(3);
			for (int i = 0; i < scored.Count && take.Count < 3; i++)
			{
				var s = scored[i].s;
				if (string.IsNullOrWhiteSpace(s)) continue;
				take.Add(s);
			}
			if (take.Count == 0) return null;
			return NormalizeOutputText(string.Join(" ", take));
		}
		catch { return null; }
	}

	private static string ChooseBestLoadingTip(List<string> lines)
	{
		try
		{
			if (lines == null || lines.Count == 0) return null;
			string best = null;
			double bestScore = double.NegativeInfinity;
			for (int i = 0; i < lines.Count; i++)
			{
				var s = lines[i];
				if (string.IsNullOrWhiteSpace(s)) continue;
				s = s.Trim();
				if (s.Length < 4) continue;
				if (s.Length > 140) continue;
				if (s.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) continue;

				// scoring: longer + more letters/CJK, penalize digits
				int digits = 0, letters = 0;
				for (int k = 0; k < s.Length; k++)
				{
					char c = s[k];
					if (char.IsDigit(c)) digits++;
					else if (char.IsLetter(c)) letters++;
				}
				double score = s.Length + letters * 2 - digits * 3;
				if (score > bestScore)
				{
					bestScore = score;
					best = s;
				}
			}
			return string.IsNullOrWhiteSpace(best) ? null : NormalizeOutputText(best);
		}
		catch { return null; }
	}

	internal void RequestSpeakNovelLine(string line, object callback, object[] args)
	{
		try
		{
			if (_isQuitting) return;
			if (string.IsNullOrWhiteSpace(line)) return;
			line = NormalizeOutputText(line);
			if (string.IsNullOrWhiteSpace(line)) return;

			_pendingNovelLine = line;
			_pendingNovelCallback = callback;
			_pendingNovelArgs = args;
			_pendingNovelSpeakAt = DateTime.Now.AddMilliseconds(35);
			_pendingNovelDeadline = DateTime.Now.AddMilliseconds(220);
		}
		catch { }
	}

	private string TryFindNovelSpeaker(string line, object callback, object[] args)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(line)) return null;
			
			bool IsComponentActiveLocal(object component)
			{
				try
				{
					if (component == null) return false;
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
							var aihObj = pAih.GetValue(go);
							if (aihObj is bool aih && !aih) return false;
						}
						var pAs = go.GetType().GetProperty("activeSelf", BindingFlags.Instance | BindingFlags.Public);
						if (pAs != null)
						{
							var asObj = pAs.GetValue(go);
							if (asObj is bool ase && !ase) return false;
						}
					}
					return true;
				}
				catch { return true; }
			}

			bool LooksLikeName(string t)
			{
				if (string.IsNullOrWhiteSpace(t)) return false;
				t = _richTextTagRegex.Replace(t, "").Trim();
				// 說話者名字通常很短、且不包含空白/句子標點；避免把旁邊的短句/提示文字誤當名字
				if (t.Length == 0 || t.Length > 10) return false;
				for (int i = 0; i < t.Length; i++)
				{
					if (char.IsWhiteSpace(t[i])) return false;
				}
				// 常見句子標點（含省略號/破折號）直接排除
				if (t.IndexOf('。') >= 0 || t.IndexOf('，') >= 0 || t.IndexOf('、') >= 0 ||
				    t.IndexOf('！') >= 0 || t.IndexOf('？') >= 0 || t.IndexOf('…') >= 0 ||
				    t.IndexOf('—') >= 0 || t.IndexOf('-') >= 0 || t.IndexOf('～') >= 0 ||
				    t.IndexOf('（') >= 0 || t.IndexOf('）') >= 0 || t.IndexOf('「') >= 0 ||
				    t.IndexOf('」') >= 0 || t.IndexOf('『') >= 0 || t.IndexOf('』') >= 0 ||
				    t.IndexOf('：') >= 0 || t.IndexOf(':') >= 0)
					return false;
				if (string.Equals(t, line, StringComparison.Ordinal)) return false;
				if (line.IndexOf(t, StringComparison.Ordinal) >= 0) return false;
				return true;
			}

			string Clean(string t)
			{
				if (string.IsNullOrWhiteSpace(t)) return null;
				t = _richTextTagRegex.Replace(t, "").Trim();
				return string.IsNullOrWhiteSpace(t) ? null : t;
			}

			// 1) 先從 args 裡找可能的名字（若 DOTextAsync 有直接傳入名字）
			if (args != null)
			{
				foreach (var a in args)
				{
					if (a == null) continue;
					if (a is string s)
					{
						s = Clean(s);
						if (LooksLikeName(s)) return s;
					}
				}
			}

			// 2) 從 callback delegate target 的 gameObject 子樹掃描 TMP_Text
			Delegate del = callback as Delegate;
			if (del != null && del.Target != null)
			{
				object target = del.Target;
				object go = null;
				try { go = target.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(target); } catch { go = null; }

				Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
				if (go != null && tmpType != null)
				{
					var m = go.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
					var arr = m?.Invoke(go, new object[] { tmpType, true }) as Array;
					if (arr != null)
					{
						string best = null;
						foreach (var comp in arr)
						{
							if (comp == null) continue;
							if (!IsComponentActiveLocal(comp)) continue;
							string t = null;
							try { t = comp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(comp) as string; } catch { t = null; }
							t = Clean(t);
							if (!LooksLikeName(t)) continue;

							// 優先找物件名含 name/speaker
							string on = null;
							try
							{
								var cgo = comp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(comp);
								on = cgo?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(cgo) as string;
							}
							catch { on = null; }

							if (!string.IsNullOrWhiteSpace(on) &&
							    (on.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0 ||
							     on.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0 ||
							     on.IndexOf("chara", StringComparison.OrdinalIgnoreCase) >= 0 ||
							     on.IndexOf("actor", StringComparison.OrdinalIgnoreCase) >= 0))
								return t;

							if (best == null || t.Length < best.Length) best = t;
						}
						if (!string.IsNullOrWhiteSpace(best)) return best;
					}
				}
			}

			// 3) 全場景掃描：找一個短文字且物件名像 name/speaker 的 TMP_Text
			try
			{
				Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
				if (tmpType == null) return null;
				Type objType = Type.GetType("UnityEngine.Object, UnityEngine.CoreModule") ?? Type.GetType("UnityEngine.Object");
				if (objType == null) return null;

				Array arr = null;
				// Unity 6: FindObjectsByType(Type, FindObjectsInactive, FindObjectsSortMode)
				var mFindByType = objType.GetMethod("FindObjectsByType", BindingFlags.Public | BindingFlags.Static, null,
					new[] { typeof(Type), Type.GetType("UnityEngine.FindObjectsInactive, UnityEngine.CoreModule") ?? typeof(int), Type.GetType("UnityEngine.FindObjectsSortMode, UnityEngine.CoreModule") ?? typeof(int) }, null);
				if (mFindByType != null)
				{
					// Include inactive, no sorting (0/0)
					arr = mFindByType.Invoke(null, new object[] { tmpType, 1, 0 }) as Array;
				}
				if (arr == null)
				{
					// Older: FindObjectsOfType(Type, bool)
					var mFind = objType.GetMethod("FindObjectsOfType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type), typeof(bool) }, null);
					arr = mFind?.Invoke(null, new object[] { tmpType, true }) as Array;
				}
				if (arr != null)
				{
					foreach (var comp in arr)
					{
						if (comp == null) continue;
						if (!IsComponentActiveLocal(comp)) continue;
						string t = null;
						try { t = comp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(comp) as string; } catch { t = null; }
						t = Clean(t);
						if (!LooksLikeName(t)) continue;

						string on = null;
						try
						{
							var cgo = comp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(comp);
							on = cgo?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(cgo) as string;
						}
						catch { on = null; }

						if (!string.IsNullOrWhiteSpace(on) &&
						    (on.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0 ||
						     on.IndexOf("speaker", StringComparison.OrdinalIgnoreCase) >= 0 ||
						     on.IndexOf("chara", StringComparison.OrdinalIgnoreCase) >= 0 ||
						     on.IndexOf("actor", StringComparison.OrdinalIgnoreCase) >= 0))
							return t;
					}
				}
			}
			catch { }
		}
		catch { }
		return null;
	}

	private void ReadSelectionAndSpeak(object selectedGameObject)
	{
		try
		{
			object val = selectedGameObject;
			if (val == null)
				return;

			string text = null;
			string text2 = null;

			// 共用：從「同層/子物件」找最像 label / value 的文字（設定頁很多值是靠旁邊的 TextMeshProUGUI 顯示）
			// 重要：這裡只掃 TMP_Text / UI.Text，避免掃整棵樹所有 Component 造成 UI 選取 lag。
			string FindNearbyText(Func<string, bool> predicate, string exclude = null)
			{
				try
				{
					// 從自己開始，往上最多兩層，找「同組」的 label/value（設定 UI 常把 Slider/Label/Value 分在一個 group）
					object currentGo = val;
					for (int depth = 0; depth < 3; depth++)
					{
						// 自己與子物件
						foreach (string s in EnumerateTextStrings(currentGo))
						{
							try
							{
								if (string.IsNullOrWhiteSpace(s)) continue;
								var st = s.Trim();
								if (!string.IsNullOrEmpty(exclude) && string.Equals(st, exclude, StringComparison.OrdinalIgnoreCase)) continue;
								if (predicate(st)) return st;
							}
							catch { }
						}

						// 同層兄弟（同一個 parent group）
						object parent = TryGetParentTransform(currentGo);
						if (parent != null)
						{
							foreach (object child in EnumerateChildrenList(parent))
							{
								try
								{
									foreach (string s in EnumerateTextStrings(child))
									{
										if (string.IsNullOrWhiteSpace(s)) continue;
										var st = s.Trim();
										if (!string.IsNullOrEmpty(exclude) && string.Equals(st, exclude, StringComparison.OrdinalIgnoreCase)) continue;
										if (predicate(st)) return st;
									}
								}
								catch { }
							}
						}

						currentGo = parent?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parent);
						if (currentGo == null) break;
					}
				}
				catch { }
				return null;
			}

			bool LooksLikeValue(string s)
			{
				if (string.IsNullOrWhiteSpace(s)) return false;
				s = s.Trim();
				// 典型值：70%、+12 ms、x 1.2、1920 x 1080、10 等
				if (s.Contains("%") || s.Contains("ms") || s.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || s.Contains(" x ")) return true;
				// 純數字
				double _;
				return double.TryParse(s, out _);
			}

			bool LooksLikeLabel(string s)
			{
				if (string.IsNullOrWhiteSpace(s)) return false;
				s = s.Trim();
				if (LooksLikeValue(s)) return false;
				// 設定選項標題可能比較長（不要太嚴格）
				return s.Length <= 40;
			}

			bool LooksLikeToggleStateText(string s)
			{
				if (string.IsNullOrWhiteSpace(s)) return false;
				s = s.Trim();
				// 只用「畫面上真的可能出現」的短狀態文字，不自己翻譯。
				if (string.Equals(s, "ON", StringComparison.OrdinalIgnoreCase)) return true;
				if (string.Equals(s, "OFF", StringComparison.OrdinalIgnoreCase)) return true;
				if (string.Equals(s, "✓", StringComparison.Ordinal) || string.Equals(s, "✔", StringComparison.Ordinal)) return true;
				if (string.Equals(s, "×", StringComparison.Ordinal) || string.Equals(s, "✗", StringComparison.Ordinal)) return true;
				return false;
			}

			int GetUnityInstanceId(object unityObj)
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
			bool IsKnownSettingsToggle(object mornUiButton)
			{
				try
				{
					int id = GetUnityInstanceId(mornUiButton);
					if (id == 0) return false;
					lock (_knownSettingsToggleIds) return _knownSettingsToggleIds.Contains(id);
				}
				catch { return false; }
			}

			// 讀取 Slider
			try
			{
				Type type2 = Type.GetType("UnityEngine.UI.Slider, UnityEngine.UI");
				if (type2 != null)
				{
					object component = TryGetComponent(val, type2);
					if (component != null)
					{
						// 優先讀旁邊真正顯示的值（例如音量 70%、時機 +12 ms、render scale x 1.2）
						string valueText = FindNearbyText(LooksLikeValue);
						// Calibration/時機調整：有些 UI 佈局把顯示值(TMP_Text)放在不同 group，nearby 掃描會抓不到，
						// 但遊戲確實用 TextMeshProUGUI `_timingText` 顯示最終文字。這裡用 TMP 文字補一次，避免退回百分比。
						if (string.IsNullOrWhiteSpace(valueText))
						{
							try
							{
								double min = Convert.ToDouble(type2.GetProperty("minValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component) ?? 0.0);
								double max = Convert.ToDouble(type2.GetProperty("maxValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component) ?? 0.0);
								// timing slider 的典型特徵：min < 0 且 max > 0（左右偏移）
								if (min < 0 && max > 0)
								{
									var timing = TryGetTimingCalibrationTmpText();
									if (!string.IsNullOrWhiteSpace(timing)) valueText = timing;
								}
							}
							catch { }
						}
						if (!string.IsNullOrWhiteSpace(valueText))
							text = valueText;

						string labelText = FindNearbyText(LooksLikeLabel, exclude: text);
						if (!string.IsNullOrWhiteSpace(labelText))
							text2 = labelText;

						// 沒找到顯示值才退回用 slider 的 value 計算（避免設定頁「值錯」）
						if (string.IsNullOrWhiteSpace(text))
					{
						object valObj = type2.GetProperty("value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
						if (valObj != null)
						{
							try
							{
								double num = Convert.ToDouble(valObj);
								PropertyInfo minProp = type2.GetProperty("minValue", BindingFlags.Instance | BindingFlags.Public);
								PropertyInfo maxProp = type2.GetProperty("maxValue", BindingFlags.Instance | BindingFlags.Public);
								if (minProp != null && maxProp != null)
								{
									double min = Convert.ToDouble(minProp.GetValue(component));
									double max = Convert.ToDouble(maxProp.GetValue(component));
									text = (max > min) ? ((int)Math.Round((num - min) / (max - min) * 100.0) + "%") : num.ToString();
								}
								else
								{
									text = num.ToString();
								}
							}
							catch
							{
								text = valObj.ToString();
								}
							}
						}
					}
				}
			}
			catch { }

			// 讀取 Toggle
			try
			{
				Type type3 = Type.GetType("UnityEngine.UI.Toggle, UnityEngine.UI");
				if (type3 != null)
				{
					object component2 = TryGetComponent(val, type3);
					if (component2 != null)
					{
						// 標題：從 UI 上找（不要硬寫）
						string labelText = FindNearbyText(LooksLikeLabel);
						if (!string.IsNullOrWhiteSpace(labelText))
						{
							// UnityEngine.UI.Toggle 在這遊戲裡常被拿來做「可選取項目/高亮」，不等於「開關」。
							// 為避免主選單/清單亂加 off/on：一律只唸文字，不輸出狀態。
							text = string.IsNullOrWhiteSpace(text) ? labelText : text;
							text2 = null;
						}
					}
				}
			}
			catch { }

			// 讀取 MornUGUIButton.AsToggle（Pastel☆Parade 設定頁的全螢幕/抗鋸齒/震動等開關）
			// 對照遊戲原始碼：_vibrationButton.AsToggle.IsOn / OnValueChanged
			try
			{
				Type btnType = AccessTools.TypeByName("MornUGUI.MornUGUIButton");
				if (btnType != null)
				{
					object btn = TryGetComponent(val, btnType);
					if (btn != null)
					{
						bool allowStateOutput = IsKnownSettingsToggle(btn);

						object asToggle = null;
						try { asToggle = btnType.GetProperty("AsToggle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(btn); }
						catch { asToggle = null; }

						// 不是所有 MornUGUIButton 都是 toggle。
						// 只有「真的可切換」的才會有 OnValueChanged（對照原始碼：AsToggle.OnValueChanged.Subscribe(...)）。
						bool isToggleLike = false;
						try
						{
							if (asToggle != null)
							{
								var pOv = asToggle.GetType().GetProperty("OnValueChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (pOv != null) isToggleLike = true;
							}
						}
						catch { isToggleLike = false; }
						// 若不是白名單的「真的設定開關」，就算 AsToggle 看起來存在也不要輸出 on/off，
						// 否則主選單/清單/一般按鈕會被誤加狀態（Start off / Apply off）。
						if (!isToggleLike || !allowStateOutput) asToggle = null;

						bool isGroupedMornToggle = false;
						try
						{
							if (asToggle != null)
							{
								var pGroup = asToggle.GetType().GetProperty("Group", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
								           ?? asToggle.GetType().GetProperty("group", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
								           ?? asToggle.GetType().GetProperty("ToggleGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (pGroup != null && pGroup.GetValue(asToggle) != null) isGroupedMornToggle = true;
							}
						}
						catch { isGroupedMornToggle = false; }

						bool? isOn = null;
						if (asToggle != null)
						{
							try
							{
								var p = asToggle.GetType().GetProperty("IsOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
								        ?? asToggle.GetType().GetProperty("isOn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (p != null && p.PropertyType == typeof(bool))
								{
									object v = p.GetValue(asToggle);
									if (v is bool b) isOn = b;
								}
							}
							catch { }
						}

						// 標題：從 UI 上找（不要硬寫）
						if (string.IsNullOrWhiteSpace(text2))
						{
							string labelText = FindNearbyText(LooksLikeLabel);
							if (!string.IsNullOrWhiteSpace(labelText))
								text2 = labelText;
						}

						// 狀態：優先讀 UI 上的 ON/OFF；沒有就用 1/0（不翻譯）
						if (string.IsNullOrWhiteSpace(text) && !isGroupedMornToggle && allowStateOutput)
						{
							string stateText = FindNearbyText(LooksLikeToggleStateText);
							if (!string.IsNullOrWhiteSpace(stateText))
								text = stateText;
							else if (isOn.HasValue)
								text = isOn.Value ? "on" : "off";
						}
					}
				}
			}
			catch { }

			// 讀取 Dropdown
			try
			{
				Type type4 = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro") ?? Type.GetType("UnityEngine.UI.Dropdown, UnityEngine.UI");
				if (type4 != null)
				{
					object component3 = TryGetComponent(val, type4);
					if (component3 != null)
					{
						// 標題：從 UI 上找（不要硬寫）
						if (string.IsNullOrWhiteSpace(text2))
						{
							string labelText = FindNearbyText(LooksLikeLabel);
							if (!string.IsNullOrWhiteSpace(labelText))
								text2 = labelText;
						}

						PropertyInfo valueProp = type4.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
						PropertyInfo optionsProp = type4.GetProperty("options", BindingFlags.Instance | BindingFlags.Public);
						int? index = valueProp?.GetValue(component3) as int?;
						IList list = optionsProp?.GetValue(component3) as IList;
						if (index.HasValue && list != null && index.Value >= 0 && index.Value < list.Count)
						{
							object optObj = list[index.Value];
							string optText = optObj.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(optObj) as string;
							if (!string.IsNullOrWhiteSpace(optText))
								text = optText;
						}
					}
				}
			}
			catch { }

			// InputField
			try
			{
				Type type5 = Type.GetType("UnityEngine.UI.InputField, UnityEngine.UI") ?? Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
				if (type5 != null)
				{
					object component4 = TryGetComponent(val, type5);
					if (component4 != null)
					{
						string txt = type5.GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component4) as string;
						if (!string.IsNullOrWhiteSpace(txt))
							text = txt;
					}
				}
			}
			catch { }

			// TMP_Text
			Type type6 = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
			if (type6 != null)
			{
				object component5 = TryGetComponent(val, type6);
				if (component5 != null)
					text = type6.GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component5) as string;
			}

			// Text fallback
			if (string.IsNullOrWhiteSpace(text))
			{
				try
				{
					Type type7 = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
					if (type7 != null)
					{
						object component6 = TryGetComponent(val, type7);
						if (component6 != null)
							text = type7.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(component6) as string;
					}
				}
				catch { }
			}

			// Children text（只取自己的文字，不要再從兄弟節點湊 label，避免語言/切頁時「先唸上一個值一次」）
			if (string.IsNullOrWhiteSpace(text))
			{
				foreach (object c in GetComponentsInChildrenList(val))
				{
					try
					{
						PropertyInfo p = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
						if (p == null) continue;
						string s = p.GetValue(c) as string;
						if (!string.IsNullOrWhiteSpace(s))
						{
							if (string.IsNullOrWhiteSpace(text))
								text = s.Trim();
						}
					}
					catch { }
				}
			}

			if (string.IsNullOrWhiteSpace(text))
				return;

			text = text.Trim();
			if ((text.StartsWith("[") && text.Contains("->")) || string.Equals(text, "Untagged", StringComparison.OrdinalIgnoreCase))
				return;

			string finalText = text;
			if (!string.IsNullOrWhiteSpace(text2))
			{
				text2 = text2.Trim();
				// 例如主選單常見的「開始 開始」：label/value 其實相同，直接去掉重複。
				if (!string.Equals(text2, text, StringComparison.OrdinalIgnoreCase))
						finalText = text2 + " " + text;
			}

			finalText = NormalizeOutputText(finalText);
			if (string.IsNullOrWhiteSpace(finalText)) return;
			if (_menuPositionAnnouncementsEnabled)
			{
				var suffix = TryBuildMenuPositionSuffix(val);
				if (!string.IsNullOrWhiteSpace(suffix))
					finalText = NormalizeOutputText(finalText + " " + suffix);
			}

			// 刪除存檔等確認對話框：焦點只在 YES/NO，正文不會自動被選到。
			// 這裡在唸 YES/NO 時，從同一個父容器抓出「非按鈕」文字並合併朗讀。
			try
			{
				string optionRaw = finalText.Trim();
				// 有時會被組成「<label> <YES/NO>」，一律取最後 token 當選項
				string optionToken = optionRaw;
				int lastSpace = optionRaw.LastIndexOf(' ');
				if (lastSpace >= 0 && lastSpace < optionRaw.Length - 1)
					optionToken = optionRaw.Substring(lastSpace + 1).Trim();

				bool IsDialogOptionToken(string tok)
				{
					if (string.IsNullOrWhiteSpace(tok)) return false;
					tok = tok.Trim();
					// English
					if (string.Equals(tok, "YES", StringComparison.OrdinalIgnoreCase)) return true;
					if (string.Equals(tok, "NO", StringComparison.OrdinalIgnoreCase)) return true;
					if (string.Equals(tok, "OK", StringComparison.OrdinalIgnoreCase)) return true;
					if (string.Equals(tok, "CANCEL", StringComparison.OrdinalIgnoreCase)) return true;
					// Japanese
					if (string.Equals(tok, "はい", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "いいえ", StringComparison.Ordinal)) return true;
					// Chinese (Traditional/Simplified)
					if (string.Equals(tok, "是", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "否", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "確定", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "取消", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "確認", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "确定", StringComparison.Ordinal)) return true;
					if (string.Equals(tok, "确认", StringComparison.Ordinal)) return true;
					return false;
				}

				bool isYesNo = IsDialogOptionToken(optionToken);
				if (isYesNo)
				{
					string body = null;
					string bundle = null;
					int rootId = 0;
					object curGo = val;
					for (int d = 0; d < 12 && curGo != null; d++)
					{
						object parent = TryGetParentTransform(curGo);
						curGo = parent?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parent);
						if (curGo == null) break;

						var texts = EnumerateTextStrings(curGo);
						if (texts == null || texts.Count == 0) continue;

						// 先收集候選：排除 YES/NO 本身與極短字
						var list = new List<string>();
						for (int i = 0; i < texts.Count; i++)
						{
							var s = NormalizeOutputText(texts[i]);
							if (string.IsNullOrWhiteSpace(s)) continue;
							if (string.Equals(s, optionRaw, StringComparison.OrdinalIgnoreCase)) continue;
							if (string.Equals(s, optionToken, StringComparison.OrdinalIgnoreCase)) continue;
							// 避免把另一顆按鈕（或同義選項）當正文
							if (IsDialogOptionToken(s)) continue;
							// 太短多半是裝飾/單字；但正文可能被拆段，保留 >= 3
							if (s.Length < 3) continue;
							list.Add(s);
						}

						if (list.Count > 0)
						{
							// 去重（保持順序）
							var uniq = new List<string>();
							for (int i = 0; i < list.Count; i++)
							{
								var s = list[i];
								bool dup = false;
								for (int j = 0; j < uniq.Count; j++)
								{
									if (string.Equals(uniq[j], s, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
								}
								if (!dup) uniq.Add(s);
							}

							// 先把所有候選合併成 bundle（避免正文被拆成多個 TMP）
							// 但避免過長：最多取前 3 段較長的字串
							uniq.Sort((a, b) => b.Length.CompareTo(a.Length));
							var take = new List<string>();
							for (int i = 0; i < uniq.Count && take.Count < 3; i++) take.Add(uniq[i]);
							bundle = NormalizeOutputText(string.Join(" ", take));

							// 同時保留單一最長段當 body（若你只想唸一段時也能用）
							body = take[0];
							try
							{
								// 把「找到文字的那一層容器」當作對話框 root
								rootId = GetUnityInstanceId(curGo);
							}
							catch { rootId = 0; }
							break;
						}
					}

					// 優先用 bundle（更完整），fallback 用 body
					var chosen = !string.IsNullOrWhiteSpace(bundle) ? bundle : body;
					if (!string.IsNullOrWhiteSpace(chosen))
					{
						var now = DateTime.Now;
						// 進入對話框時唸一次正文；之後移動按鈕只唸 YES/NO
						bool isNewDialog =
							_dialogRootInstanceId == 0 ||
							(now > _dialogContextUntil) ||
							// 若之前沒有成功記到正文，這次一定要當成新對話框處理
							string.IsNullOrWhiteSpace(_dialogBundleText) ||
							(rootId != 0 && _dialogRootInstanceId != 0 && rootId != _dialogRootInstanceId) ||
							(!string.Equals(_dialogBundleText, chosen, StringComparison.Ordinal));

						if (isNewDialog)
						{
							_dialogRootInstanceId = (rootId != 0) ? rootId : _dialogRootInstanceId;
							_dialogBundleText = chosen;
							_dialogBundleSpokenAt = now;
							_dialogContextUntil = now.AddSeconds(30);

							// 第一次：正文 + 目前焦點按鈕一起唸
							finalText = NormalizeOutputText(chosen + " " + optionToken);
						}
						else
						{
							// 後續：只唸按鈕
							_dialogContextUntil = now.AddSeconds(30);
							finalText = optionToken;
						}
					}
				}
				else
				{
					// 離開 YES/NO：若太久沒再遇到，就讓對話框 context 自然過期
					if (_dialogRootInstanceId != 0 && DateTime.Now > _dialogContextUntil)
					{
						_dialogRootInstanceId = 0;
						_dialogBundleText = null;
						_dialogBundleSpokenAt = DateTime.MinValue;
						_dialogContextUntil = DateTime.MinValue;
					}
				}
			}
			catch { }

			// 先送出 selection 朗讀，再進入 suppress window
			Instance?.SendToTolk(finalText);
			try
			{
				_lastSelectionReadAt = DateTime.Now;
				_suppressTextSendsUntil = DateTime.Now.AddMilliseconds(800.0);
			}
			catch { }
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: ReadCurrentSelectionAndSpeak exception: " + ex);
		}
	}

	private List<string> EnumerateTextStrings(object gameObject)
	{
		// 只列舉 TMP_Text / UI.Text 的文字，避免大量非必要 component 掃描造成 lag
		var list = new List<string>();
		try
		{
			if (gameObject == null) return list;

			Type tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
			Type uiTextType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");

			if (tmpType != null)
			{
				Array arr = GetComponentsInChildrenArray(gameObject, tmpType);
				if (arr != null)
				{
					foreach (var o in arr)
					{
						if (o == null) continue;
						string s = o.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(o) as string;
						if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
					}
				}
			}
			if (uiTextType != null)
			{
				Array arr = GetComponentsInChildrenArray(gameObject, uiTextType);
				if (arr != null)
				{
					foreach (var o in arr)
					{
						if (o == null) continue;
						string s = o.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o) as string;
						if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
					}
				}
			}
		}
		catch { }
		return list;
	}

	private void ReadCurrentSelectionAndSpeak()
	{
		try
		{
			Type type = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI")
			            ?? Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.CoreModule")
			            ?? Type.GetType("UnityEngine.EventSystems.EventSystem");
			object obj = null;
			if (type != null)
			{
				object obj2 = type.GetProperty("current", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				if (obj2 != null)
					obj = type.GetProperty("currentSelectedGameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj2);
			}
			ReadSelectionAndSpeak(obj);
		}
		catch { }
	}

	// Calibration menu: prefer the TMP text the game uses (`UITimingSettingState._timingText`)
	private string TryGetTimingCalibrationTmpText()
	{
		try
		{
			Type tState = AccessTools.TypeByName("PastelParade.UITimingSettingState");
			if (tState == null) return null;
			Array arr = FindObjectsOfTypeCompat(tState, includeInactive: true);
			if (arr == null || arr.Length == 0) return null;
			for (int i = 0; i < arr.Length; i++)
			{
				var st = arr.GetValue(i);
				if (st == null) continue;
				var go = st.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(st);
				if (go == null) continue;
				var aih = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go) as bool?;
				if (aih.HasValue && !aih.Value) continue;

				object tmp = st.GetType().GetField("_timingText", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(st);
				if (tmp == null) continue;
				string s = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tmp) as string;
				s = NormalizeOutputText(_richTextTagRegex.Replace(s ?? "", "").Trim());
				if (string.IsNullOrWhiteSpace(s)) continue;
				// must look like timing value
				if (s.IndexOf("ms", StringComparison.OrdinalIgnoreCase) < 0) continue;
				return s;
			}
		}
		catch { }
		return null;
	}

	internal void OnPatchesApplied()
	{
		try { TriggerReadSelection(); } catch { }
	}

	internal void TriggerReadSelection(object target = null)
	{
		try
		{
			if (_isQuitting) return;
			// 翻頁合併輸出期間：不要直接朗讀 selection（避免「先唸焦點、再唸合併」）
			// 而是把焦點內容塞回合併輸出。
			if (IsTabMergePending || DateTime.Now < _suppressSelectionUntil)
			{
				try
				{
					var focus = Patches.TryBuildFocusTextFromSelection();
					if (!string.IsNullOrWhiteSpace(focus))
						SetPendingTabFocusText(focus);
				}
				catch { }
				return;
			}
			// 直接唸：不要延遲（用 __0 目標可以避免唸到上一個）
			if (target != null) ReadSelectionAndSpeak(target);
			else ReadCurrentSelectionAndSpeak();
		}
		catch { }
	}

	internal void SuppressSelectionFor(int milliseconds)
	{
		try
		{
			if (_isQuitting) return;
			if (milliseconds <= 0) return;
			var until = DateTime.Now.AddMilliseconds(milliseconds);
			// 取較晚者，避免覆蓋更長的抑制（例如翻頁合併）
			if (_suppressSelectionUntil < until) _suppressSelectionUntil = until;
		}
		catch { }
	}

	internal bool IsTabMergePending => !string.IsNullOrWhiteSpace(_pendingTabTitle);

	internal void RequestSpeakTabTitleMerged(string tabTitle)
	{
		try
		{
			if (_isQuitting) return;
			tabTitle = NormalizeOutputText(tabTitle);
			if (string.IsNullOrWhiteSpace(tabTitle)) return;
			_pendingTabTitle = tabTitle;
			_pendingTabFocus = null;
			_pendingTabSpeakAt = DateTime.Now.AddMilliseconds(60);
			_pendingTabDeadline = DateTime.Now.AddMilliseconds(450);
			// 抑制時間要覆蓋整個合併窗口，否則 UI 更新較慢時會先唸焦點、再唸合併
			_suppressSelectionUntil = _pendingTabDeadline;
		}
		catch { }
	}

	internal void SetPendingTabFocusText(string focusText)
	{
		try
		{
			if (_isQuitting) return;
			if (string.IsNullOrWhiteSpace(_pendingTabTitle)) return;
			focusText = NormalizeOutputText(focusText);
			if (string.IsNullOrWhiteSpace(focusText)) return;
			_pendingTabFocus = focusText;
		}
		catch { }
	}

	private string GetSettingsCategoryTitleCandidate(object context)
	{
		try
		{
			if (_isQuitting) return null;

			// 先找「此 state 所在的 Canvas」作為掃描範圍，避免切頁瞬間掃到上一頁標題。
			// 若找不到 context/canvas，才退回全場景 FindObjectsOfType。
			// - activeInHierarchy
			// - 先 strip rich-text tag
			// - 不是 value（%/ms/x/解析度/純數字）
			// - fontSize 越大越像標題；同分時取畫面越上方
			var tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") ?? Type.GetType("TMPro.TMP_Text");
			var uiTextType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
			if (tmpType == null && uiTextType == null) return null;

			// 兩種文字元件都收集起來（語言頁可能不是 TMP）
			var comps = new List<object>(128);
			object rootCanvasGo = null;
			try
			{
				// context -> gameObject -> 找到上層 Canvas
				var ctxGo = context?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(context);
				if (ctxGo != null)
				{
					// 語言頁的「分類/標題」很常在更外層的共用 Header Canvas（不一定在頁面內容的 Canvas 之下）
					var tn = context?.GetType().FullName ?? "";
					rootCanvasGo = (tn == "PastelParade.UILocalizeState")
						? (FindTopmostCanvasGameObject(ctxGo) ?? FindRootCanvasGameObject(ctxGo))
						: FindRootCanvasGameObject(ctxGo);
					if (rootCanvasGo != null)
					{
						if (tmpType != null)
						{
							var a = GetComponentsInChildrenArray(rootCanvasGo, tmpType);
							if (a != null) foreach (var o in a) if (o != null) comps.Add(o);
						}
						if (uiTextType != null)
						{
							var a = GetComponentsInChildrenArray(rootCanvasGo, uiTextType);
							if (a != null) foreach (var o in a) if (o != null) comps.Add(o);
						}
					}
				}
			}
			catch { }

			if (comps.Count == 0)
			{
				// 有些 State 不是 MonoBehaviour（拿不到 gameObject），就只能全場景掃描。
				// Unity 版本差異很大：用相容 API（FindObjectsByType / FindObjectsOfType(Type,bool) / FindObjectsOfType(Type)）
				if (tmpType != null)
				{
					var a = FindObjectsOfTypeCompat(tmpType, includeInactive: true);
					if (a != null) foreach (var o in a) if (o != null) comps.Add(o);
				}
				if (uiTextType != null)
				{
					var a = FindObjectsOfTypeCompat(uiTextType, includeInactive: true);
					if (a != null) foreach (var o in a) if (o != null) comps.Add(o);
				}
				if (comps.Count == 0) return null;
			}

			string best = null;
			float bestScore = float.NegativeInfinity;
			string bestNonSelectable = null;
			float bestNonSelectableScore = float.NegativeInfinity;

			for (int i = 0; i < comps.Count; i++)
			{
				var c = comps[i];
				if (c == null) continue;

				try
				{
					// activeInHierarchy
					var go = c.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					if (go == null) continue;
					var aih = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go) as bool?;
					if (aih.HasValue && !aih.Value) continue;

					var sRaw = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c) as string;
					if (string.IsNullOrWhiteSpace(sRaw)) continue;
					var s = _richTextTagRegex.Replace(sRaw, "").Trim();
					if (string.IsNullOrWhiteSpace(s)) continue;

					// 過濾常見 value
					if (s.Contains("%") || s.Contains("ms") || s.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || s.Contains(" x "))
						continue;
					double _;
					if (double.TryParse(s, out _)) continue;

					// 長度過長就不當標題（避免把整段描述當標題）
					if (s.Length > 32) continue;

					// fontSize
					float fontSize = 0f;
					try
					{
						var fs = c.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
						if (fs is float f) fontSize = f;
						else if (fs is double d) fontSize = (float)d;
					}
					catch { }

					// 位置：RectTransform.position.y（比 anchoredPosition 更通用）
					float y = 0f;
					var rt = c.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(c);
					if (rt != null)
					{
						var pos = rt.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rt);
						if (pos != null)
						{
							var py = pos.GetType().GetField("y")?.GetValue(pos);
							if (py is float fy) y = fy;
							else if (py is double dy) y = (float)dy;
						}
					}

					// 分數：fontSize 權重大、y 次之
					float score = fontSize * 1000f + y;

					// 優先排除「可選取元件（Button/Toggle/Slider…）底下的文字」：
					// 否則語言頁很容易把「日本語/English/中文…」誤判成分類標題，
					// 然後又剛好和焦點按鈕相同，被 SendToTolk 的 250ms 去重吞掉，造成「分類完全沒唸」的體感。
					bool underSelectable = false;
					try { underSelectable = IsUnderSelectable(c); } catch { underSelectable = false; }
					if (!underSelectable && score > bestNonSelectableScore)
					{
						bestNonSelectableScore = score;
						bestNonSelectable = s;
					}

					if (score > bestScore)
					{
						bestScore = score;
						best = s;
					}
				}
				catch { }
			}

			// 先回傳非 selectable 底下的候選；真的找不到才退回任意文字（避免完全不唸）
			if (!string.IsNullOrWhiteSpace(bestNonSelectable)) return bestNonSelectable;
			return string.IsNullOrWhiteSpace(best) ? null : best;
		}
		catch { }
		return null;
	}

	private static Array FindObjectsOfTypeCompat(Type componentType, bool includeInactive)
	{
		try
		{
			if (componentType == null) return null;
			Type objType = Type.GetType("UnityEngine.Object, UnityEngine.CoreModule") ?? Type.GetType("UnityEngine.Object");
			if (objType == null) return null;

			// Unity 6+: FindObjectsByType(Type, FindObjectsInactive, FindObjectsSortMode)
			try
			{
				var inactiveEnum = Type.GetType("UnityEngine.FindObjectsInactive, UnityEngine.CoreModule");
				var sortEnum = Type.GetType("UnityEngine.FindObjectsSortMode, UnityEngine.CoreModule");
				if (inactiveEnum != null && sortEnum != null)
				{
					var m = objType.GetMethod(
						"FindObjectsByType",
						BindingFlags.Public | BindingFlags.Static,
						null,
						new[] { typeof(Type), inactiveEnum, sortEnum },
						null
					);
					if (m != null)
					{
						// includeInactive: 1 = Include, 0 = Exclude; sort: 0 = None
						var inactiveVal = Enum.ToObject(inactiveEnum, includeInactive ? 1 : 0);
						var sortVal = Enum.ToObject(sortEnum, 0);
						return m.Invoke(null, new object[] { componentType, inactiveVal, sortVal }) as Array;
					}
				}
			}
			catch { }

			// Older: FindObjectsOfType(Type, bool)
			try
			{
				var m = objType.GetMethod(
					"FindObjectsOfType",
					BindingFlags.Public | BindingFlags.Static,
					null,
					new[] { typeof(Type), typeof(bool) },
					null
				);
				if (m != null) return m.Invoke(null, new object[] { componentType, includeInactive }) as Array;
			}
			catch { }

			// Oldest: FindObjectsOfType(Type)
			try
			{
				var m = objType.GetMethod(
					"FindObjectsOfType",
					BindingFlags.Public | BindingFlags.Static,
					null,
					new[] { typeof(Type) },
					null
				);
				if (m != null) return m.Invoke(null, new object[] { componentType }) as Array;
			}
			catch { }
		}
		catch { }
		return null;
	}

	internal void RequestSpeakSettingsCategoryTitle(object stateInstance = null, bool fromRebuild = false)
	{
		if (_isQuitting) return;
		// 進入設定頁時，slider 會先被程式設值一輪；先忽略短時間，避免初始化就亂唸。
		_ignoreSliderChangesUntil = DateTime.Now.AddMilliseconds(80);

		_pendingCategoryContext = stateInstance;
		_pendingCategoryLastCandidate = null;
		_pendingCategoryStableCount = 0;
		_pendingCategoryTitleSpeak = true;
		_pendingCategoryTries = 0;
		_pendingCategoryCapturedBest = null;
		_pendingCategoryCapturedBestScore = float.NegativeInfinity;
		_pendingCategoryCapturedBestNonSelectable = null;
		_pendingCategoryCapturedBestNonSelectableScore = float.NegativeInfinity;
		// 語言頁布局較特殊，標題可能晚一點才穩定；拉長一點避免完全不唸
		try
		{
			var tn = stateInstance?.GetType().FullName ?? "";
			_pendingCategoryDeadline = (tn == "PastelParade.UILocalizeState")
				? DateTime.Now.AddMilliseconds(900)
				: (fromRebuild ? DateTime.Now.AddMilliseconds(700) : DateTime.Now.AddMilliseconds(400));
		}
		catch
		{
			_pendingCategoryDeadline = fromRebuild ? DateTime.Now.AddMilliseconds(700) : DateTime.Now.AddMilliseconds(400);
		}
		// 不要拉太長，先試一次；若不穩定再短重試
		_pendingCategoryTitleSpeakAt = DateTime.Now.AddMilliseconds(fromRebuild ? 30 : 20);
	}

	internal void OnSliderValueChanged(object sliderComponent)
	{
		try
		{
			if (_isQuitting) return;
			if (DateTime.Now < _ignoreSliderChangesUntil) return;
			if (sliderComponent == null) return;

			// 只有當該 Slider 與目前選取物件「相關」時才朗讀（避免程式背景更新亂唸）
			var sliderGo = sliderComponent.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(sliderComponent);
			if (sliderGo == null) return;

			object selected = null;
			try
			{
				Type esType = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI")
				             ?? Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.CoreModule")
				             ?? Type.GetType("UnityEngine.EventSystems.EventSystem");
				object es = esType?.GetProperty("current", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				selected = esType?.GetProperty("currentSelectedGameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(es);
			}
			catch { }
			bool related = false;
			if (selected != null)
			{
				// selected 常是 handle 子物件；也可能是某個 group 父節點
				related = IsSameOrDescendant(selected, sliderGo) || IsSameOrDescendant(sliderGo, selected);
			}
			// 沒有 selection 的情況（或 selection 不可靠）：
			// 只要使用者剛剛有在操作 UI（近期有朗讀過選取），就仍然視為使用者行為。
			if (!related && (DateTime.Now - _lastSelectionReadAt).TotalMilliseconds > 1200)
				return;

			// 延到下一個 update：確保 _seText/_bgmText/_scaleText 等顯示文字已更新
			_pendingSliderGo = sliderGo;
			_pendingSliderSpeakAt = DateTime.Now.AddMilliseconds(1);
		}
		catch { }
	}

	internal void OnToggleValueChanged(object toggleComponent)
	{
		try
		{
			if (_isQuitting) return;
			if (DateTime.Now < _ignoreSliderChangesUntil) return; // 共用：進頁初始化先略過
			if (toggleComponent == null) return;
			var toggleGo = toggleComponent.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(toggleComponent);
			if (toggleGo == null) return;
			_pendingToggleGo = toggleGo;
			_pendingToggleSpeakAt = DateTime.Now.AddMilliseconds(1);
		}
		catch { }
	}

	private static bool IsSameOrDescendant(object childGo, object parentGo)
	{
		try
		{
			if (ReferenceEquals(childGo, parentGo)) return true;
			var tr = childGo?.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(childGo);
			if (tr == null) return false;
			while (tr != null)
			{
				var go = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				if (ReferenceEquals(go, parentGo)) return true;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
		}
		catch { }
		return false;
	}

	private static object FindRootCanvasGameObject(object anyGo)
	{
		try
		{
			if (anyGo == null) return null;
			Type canvasType = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule")
			                 ?? Type.GetType("UnityEngine.Canvas, UnityEngine.UI")
			                 ?? Type.GetType("UnityEngine.Canvas");
			if (canvasType == null) return null;

			var tr = anyGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(anyGo);
			while (tr != null)
			{
				var go = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				if (go == null) break;
				// 有 Canvas component 就視為 root
				var hasCanvas = go.GetType().GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(go, new object[] { canvasType });
				if (hasCanvas != null) return go;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
		}
		catch { }
		return null;
	}

	private static object FindTopmostCanvasGameObject(object anyGo)
	{
		try
		{
			if (anyGo == null) return null;
			Type canvasType = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule")
			                 ?? Type.GetType("UnityEngine.Canvas, UnityEngine.UI")
			                 ?? Type.GetType("UnityEngine.Canvas");
			if (canvasType == null) return null;

			object lastCanvasGo = null;
			var tr = anyGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(anyGo);
			int guard = 0;
			while (tr != null && guard++ < 64)
			{
				var go = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				if (go == null) break;
				var hasCanvas = go.GetType().GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(go, new object[] { canvasType });
				if (hasCanvas != null) lastCanvasGo = go;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
			return lastCanvasGo;
		}
		catch { }
		return null;
	}

	private static Array GetComponentsInChildrenArray(object rootGo, Type componentType)
	{
		try
		{
			if (rootGo == null || componentType == null) return null;
			var comp = rootGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rootGo);
			if (comp == null) return null;

			// Component.GetComponentsInChildren(Type, bool)
			var mi = comp.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
			if (mi != null)
			{
				return mi.Invoke(comp, new object[] { componentType, true }) as Array;
			}
			// fallback：GetComponentsInChildren(Type)
			mi = comp.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type) });
			if (mi != null)
			{
				return mi.Invoke(comp, new object[] { componentType }) as Array;
			}
		}
		catch { }
		return null;
	}

	private static object FindCanvasRootGo(object anyGo)
	{
		try
		{
			if (anyGo == null) return null;
			Type canvasType = Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule")
			                 ?? Type.GetType("UnityEngine.Canvas, UnityEngine.UI")
			                 ?? Type.GetType("UnityEngine.Canvas");
			if (canvasType == null) return null;

			var tr = anyGo.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(anyGo);
			while (tr != null)
			{
				var go = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				if (go == null) break;
				var hasCanvas = go.GetType().GetMethod("GetComponent", new[] { typeof(Type) })?.Invoke(go, new object[] { canvasType });
				if (hasCanvas != null) return go;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
		}
		catch { }
		return null;
	}

	internal void EnterResultContextFromView(object resultView)
	{
		try
		{
			if (resultView == null) return;
			var go = resultView.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(resultView);
			var canvasRoot = FindCanvasRootGo(go) ?? go;
			_resultRootInstanceId = GetUnityInstanceId(canvasRoot);
			// 結果畫面停留時間較長，用 deadline 防止卡住狀態（離開結果場景後也會被 Clear）
			_resultContextUntil = DateTime.Now.AddMinutes(5);

			// 這些欄位在 ResultView.SetUp 會被同步 set_text，我們已在總結一次唸過；
			// result-only 的 set_text 監聽不應該再把它們當作「等級/評語」唸一次。
			try
			{
				string[] fields =
				{
					"_areaNumLabel", "_areaLabel", "_gameLabel",
					"_clearRate", "_perfectCount", "_fastCount", "_lateCount", "_missCount"
				};
				lock (_resultKnownValueTextIds)
				{
					_resultKnownValueTextIds.Clear();
					for (int i = 0; i < fields.Length; i++)
					{
						var f = resultView.GetType().GetField(fields[i], BindingFlags.Instance | BindingFlags.NonPublic);
						var v = f?.GetValue(resultView);
						int id = GetUnityInstanceId(v);
						if (id != 0) _resultKnownValueTextIds.Add(id);
					}
				}
			}
			catch { }
		}
		catch { }
	}

	internal void ClearResultContext()
	{
		try
		{
			_resultRootInstanceId = 0;
			_resultContextUntil = DateTime.MinValue;
			lock (_resultKnownValueTextIds) { _resultKnownValueTextIds.Clear(); }
		}
		catch { }
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

	private bool IsUnderResultRoot(object component)
	{
		try
		{
			if (_resultRootInstanceId == 0) return false;
			if (DateTime.Now > _resultContextUntil) return false;
			if (component == null) return false;
			var go = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
			if (go == null) return false;

			var tr = go.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
			int guard = 0;
			while (tr != null && guard++ < 64)
			{
				var curGo = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				if (GetUnityInstanceId(curGo) == _resultRootInstanceId) return true;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
		}
		catch { }
		return false;
	}

	private static bool HasComponent(object gameObject, Type componentType)
	{
		try
		{
			if (gameObject == null || componentType == null) return false;
			var mi = gameObject.GetType().GetMethod("GetComponent", new[] { typeof(Type) });
			return mi?.Invoke(gameObject, new object[] { componentType }) != null;
		}
		catch { return false; }
	}

	private bool IsUnderSelectable(object component)
	{
		try
		{
			if (component == null) return false;
			var go = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
			if (go == null) return false;

			// UnityEngine.UI.Selectable covers Button/Toggle/Slider/etc
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI")
			                    ?? Type.GetType("UnityEngine.UI.Selectable");
			if (selectableType == null) return false;

			var tr = go.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
			int guard = 0;
			while (tr != null && guard++ < 24)
			{
				var curGo = tr.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
				if (HasComponent(curGo, selectableType)) return true;
				tr = tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
			}
		}
		catch { }
		return false;
	}

	internal void SuppressTextSendsFor(int milliseconds)
	{
		try { _suppressTextSendsUntil = DateTime.Now.AddMilliseconds(milliseconds); } catch { }
	}

	internal void ScheduleParadeCreditSpeak(object paradeCreditInstance, object creditInfo)
	{
		try
		{
			if (_isQuitting) return;
			if (paradeCreditInstance == null || creditInfo == null) return;

			// 依原始碼：ParadeCredit.InitializeAsync 會先 Delay(CreditInDelay) 才寫入 _text.text
			int delayMs = 200;
			try
			{
				var paradeSettings = paradeCreditInstance.GetType().GetField("_paradeSettings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(paradeCreditInstance);
				var d = paradeSettings?.GetType().GetField("CreditInDelay", BindingFlags.Instance | BindingFlags.Public)?.GetValue(paradeSettings);
				if (d is float f) delayMs = Math.Max(0, (int)(f * 1000f));
				else if (d is double dd) delayMs = Math.Max(0, (int)(dd * 1000.0));
			}
			catch { delayMs = 200; }

			string lang = null;
			try
			{
				var saveMgr = paradeCreditInstance.GetType().GetField("_saveManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(paradeCreditInstance);
				var current = saveMgr?.GetType().GetProperty("Current", BindingFlags.Instance | BindingFlags.Public)?.GetValue(saveMgr);
				lang = current?.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public)?.GetValue(current) as string;
			}
			catch { lang = null; }

			string text = null;
			try
			{
				var mGet = creditInfo.GetType().GetMethod("GetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (mGet != null && !string.IsNullOrWhiteSpace(lang))
					text = mGet.Invoke(creditInfo, new object[] { lang }) as string;
			}
			catch { text = null; }

			text = _richTextTagRegex.Replace(text ?? "", "").Trim();
			if (string.IsNullOrWhiteSpace(text) || text.Length <= 1) return;

			// 排程：比原始碼 delay 稍微再晚一點點，確保 UI 已顯示
			_pendingParadeCreditText = NormalizeOutputText(text);
			_pendingParadeCreditSpeakAt = DateTime.Now.AddMilliseconds(delayMs + 30);
		}
		catch { }
	}

	internal void NotifyResultTextChanged(object component, string newText)
	{
		try
		{
			if (_isQuitting) return;
			if (!IsUnderResultRoot(component)) return;
			// ResultView.SetUp 會同步寫入這些已知欄位，避免被 result-only hook 重複唸
			try
			{
				int cid = GetUnityInstanceId(component);
				if (cid != 0)
				{
					lock (_resultKnownValueTextIds)
					{
						if (_resultKnownValueTextIds.Contains(cid)) return;
					}
				}
			}
			catch { }
			// 按鈕文字（例如 OK）會走 set_text，但那不是「等級/評語」，排除
			if (IsUnderSelectable(component)) return;
			if (DateTime.Now < _suppressTextSendsUntil) return;
			if (string.IsNullOrWhiteSpace(newText)) return;

			var s = NormalizeOutputText(newText);
			if (string.IsNullOrWhiteSpace(s)) return;

			// 結果畫面常有一段「訊息/提示」文字（例如 log 看到的 MessageText），通常不在 ResultView.SetUp 那幾個欄位中。
			// 這裡用 UI 物件名稱做精準放行：只在 Result root 底下、且物件名像 Message/Hint 的文字才允許較長內容。
			bool isMessageLike = false;
			try
			{
				var go = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
				var name = go?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go) as string;
				name = (name ?? "").Trim();
				if (name.Length > 0)
				{
					// 常見：MessageText / HintText
					if (name.IndexOf("Message", StringComparison.OrdinalIgnoreCase) >= 0) isMessageLike = true;
					else if (name.IndexOf("Hint", StringComparison.OrdinalIgnoreCase) >= 0) isMessageLike = true;
				}
			}
			catch { isMessageLike = false; }

			// 避免結果畫面大量數字更新/重設時洗語音：只針對「像是等級/評語」的短文字
			// - 太長的多半是說明/背景文字
			// - 含數字/百分比的多半是 clearRate/count（我們已在 SetUp 總結唸過）
			if (!isMessageLike)
			{
				if (s.Length > 24) return;
				for (int i = 0; i < s.Length; i++)
				{
					if (char.IsDigit(s[i])) return;
				}
				if (s.Contains("%") || s.Contains("ms")) return;
			}
			else
			{
				// 訊息/提示：允許更長，但仍做個上限，避免把整段背景敘述洗出來
				if (s.Length > 200) return;
			}

			SendToTolk(s);
		}
		catch { }
	}

	internal void NotifySettingsCategoryTitleTextChanged(object component, string newText)
	{
		try
		{
			if (_isQuitting) return;
			if (!_pendingCategoryTitleSpeak) return;
			// 只在「剛進入設定分類」的短窗口內收集（避免全域 set_text 洗爆）
			if (_pendingCategoryDeadline == DateTime.MinValue) return;
			if (DateTime.Now > _pendingCategoryDeadline) return;
			if (component == null) return;
			if (string.IsNullOrWhiteSpace(newText)) return;

			// activeInHierarchy
			var go = component.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
			if (go == null) return;
			var aih = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go) as bool?;
			if (aih.HasValue && !aih.Value) return;

			var s = _richTextTagRegex.Replace(newText, "").Trim();
			if (string.IsNullOrWhiteSpace(s)) return;

			// 過濾常見 value
			if (s.Contains("%") || s.Contains("ms") || s.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || s.Contains(" x "))
				return;
			double _;
			if (double.TryParse(s, out _)) return;
			if (s.Length > 32) return;

			// 位置/字體：挑「看起來最像標題」的那個
			float fontSize = 0f;
			try
			{
				var fs = component.GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
				if (fs is float f) fontSize = f;
				else if (fs is double d) fontSize = (float)d;
			}
			catch { }

			float y = 0f;
			try
			{
				var rt = component.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(component);
				if (rt != null)
				{
					var pos = rt.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(rt);
					if (pos != null)
					{
						var py = pos.GetType().GetField("y")?.GetValue(pos);
						if (py is float fy) y = fy;
						else if (py is double dy) y = (float)dy;
					}
				}
			}
			catch { }

			float score = fontSize * 1000f + y;
			if (score > _pendingCategoryCapturedBestScore)
			{
				_pendingCategoryCapturedBestScore = score;
				_pendingCategoryCapturedBest = s;
			}

			bool underSelectable = false;
			try { underSelectable = IsUnderSelectable(component); } catch { underSelectable = false; }
			if (!underSelectable && score > _pendingCategoryCapturedBestNonSelectableScore)
			{
				_pendingCategoryCapturedBestNonSelectableScore = score;
				_pendingCategoryCapturedBestNonSelectable = s;
			}
		}
		catch { }
	}

	private static string TryGetName(object gameObject)
	{
		try { return gameObject?.GetType().GetProperty("name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(gameObject) as string; }
		catch { return null; }
	}

	private static string TryBuildMenuPositionSuffix(object selectedGameObject)
	{
		try
		{
			if (selectedGameObject == null) return null;
			Type selectableType = Type.GetType("UnityEngine.UI.Selectable, UnityEngine.UI")
			                    ?? Type.GetType("UnityEngine.UI.Selectable");
			if (selectableType == null) return null;

			object selectedSelectable = TryGetComponent(selectedGameObject, selectableType)
			                       ?? FindComponentInParents(selectedGameObject, selectableType, 8);
			if (selectedSelectable == null) return null;

			object selectedSelectableGo = selectedSelectable.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(selectedSelectable);
			if (selectedSelectableGo == null) return null;
			int selectedId = 0;
			try
			{
				selectedId = (int)selectedSelectable.GetType().GetMethod("GetInstanceID", BindingFlags.Instance | BindingFlags.Public)?.Invoke(selectedSelectable, null);
			}
			catch { selectedId = 0; }
			if (selectedId == 0) return null;

			var candidates = new List<(int id, float x, float y)>();
			var seen = new HashSet<int>();

			object rootGo = selectedSelectableGo;
			for (int depth = 0; depth < 9; depth++)
			{
				candidates.Clear();
				seen.Clear();

				var mi = rootGo.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
				var arr = mi?.Invoke(rootGo, new object[] { selectableType, true }) as Array;
				if (arr != null)
				{
					for (int i = 0; i < arr.Length; i++)
					{
						var comp = arr.GetValue(i);
						if (comp == null) continue;

						try
						{
							int id = (int)comp.GetType().GetMethod("GetInstanceID", BindingFlags.Instance | BindingFlags.Public)?.Invoke(comp, null);
							if (id == 0 || !seen.Add(id)) continue;

							var go = comp.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(comp);
							if (go == null) continue;
							var aih = go.GetType().GetProperty("activeInHierarchy", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
							if (aih is bool activeInHierarchy && !activeInHierarchy) continue;

							var pInteractable = comp.GetType().GetProperty("interactable", BindingFlags.Instance | BindingFlags.Public);
							if (pInteractable != null)
							{
								var interactableObj = pInteractable.GetValue(comp);
								if (interactableObj is bool interactable && !interactable) continue;
							}

							float x = 0f;
							float y = 0f;
							try
							{
								var tr = go.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(go);
								var pos = tr?.GetType().GetProperty("position", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
								if (pos != null)
								{
									x = Convert.ToSingle(pos.GetType().GetField("x")?.GetValue(pos) ?? 0f);
									y = Convert.ToSingle(pos.GetType().GetField("y")?.GetValue(pos) ?? 0f);
								}
							}
							catch { }

							candidates.Add((id, x, y));
						}
						catch { }
					}
				}

				if (candidates.Count >= 2 && candidates.Count <= 40)
				{
					bool hasSelected = false;
					for (int i = 0; i < candidates.Count; i++)
					{
						if (candidates[i].id == selectedId)
						{
							hasSelected = true;
							break;
						}
					}

					if (hasSelected)
					{
						candidates.Sort((a, b) =>
						{
							int cy = (-a.y).CompareTo(-b.y);
							if (cy != 0) return cy;
							return a.x.CompareTo(b.x);
						});

						int pos = -1;
						for (int i = 0; i < candidates.Count; i++)
						{
							if (candidates[i].id == selectedId)
							{
								pos = i + 1;
								break;
							}
						}
						if (pos <= 0) return null;

						string fmt = Loc.Get("menu_position_suffix");
						try
						{
							return string.Format(fmt, pos, candidates.Count);
						}
						catch
						{
							return pos + " of " + candidates.Count;
						}
					}
				}

				var parentTr = TryGetParentTransform(rootGo);
				var parentGo = parentTr?.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(parentTr);
				if (parentGo == null) break;
				rootGo = parentGo;
			}
		}
		catch
		{
		}
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

	private static object FindComponentInParents(object gameObject, Type componentType, int maxDepth)
	{
		try
		{
			if (gameObject == null || componentType == null) return null;
			object curGo = gameObject;
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

	private static List<object> GetComponentsInChildrenList(object gameObject)
	{
		var list = new List<object>();
		try
		{
			if (gameObject == null) return list;
			var componentType = AccessTools.TypeByName("UnityEngine.Component");
			if (componentType == null) return list;
			var mi = gameObject.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
			if (mi == null) return list;
			var arr = mi.Invoke(gameObject, new object[] { componentType, true }) as Array;
			if (arr == null) return list;
			foreach (var o in arr) if (o != null) list.Add(o);
			return list;
		}
		catch { return list; }
	}

	private static object TryGetParentTransform(object gameObject)
	{
		try
		{
			if (gameObject == null) return null;
			var tr = gameObject.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public)?.GetValue(gameObject);
			if (tr == null) return null;
			return tr.GetType().GetProperty("parent", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tr);
		}
		catch { return null; }
	}

	private static List<object> EnumerateChildrenList(object transform)
	{
		var list = new List<object>();
		try
		{
			if (transform == null) return list;
			var t = transform.GetType();
			var pCount = t.GetProperty("childCount", BindingFlags.Instance | BindingFlags.Public);
			var miGetChild = t.GetMethod("GetChild", BindingFlags.Instance | BindingFlags.Public);
			if (pCount == null || miGetChild == null) return list;
			int count = 0;
			try { count = (int)pCount.GetValue(transform); } catch { }
			for (int i = 0; i < count; i++)
			{
				object child = null;
				try { child = miGetChild.Invoke(transform, new object[] { i }); } catch { }
				if (child != null) list.Add(child);
			}
			return list;
		}
		catch { return list; }
	}

	private static object GetFirstComponentInChildren(object transform)
	{
		try
		{
			if (transform == null) return null;
			var go = transform.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(transform);
			if (go == null) return null;
			var componentType = AccessTools.TypeByName("UnityEngine.Component");
			if (componentType == null) return null;
			var mi = go.GetType().GetMethod("GetComponentsInChildren", new[] { typeof(Type), typeof(bool) });
			if (mi == null) return null;
			var arr = mi.Invoke(go, new object[] { componentType, true }) as Array;
			if (arr == null || arr.Length == 0) return null;
			for (int i = 0; i < arr.Length; i++)
			{
				var c = arr.GetValue(i);
				if (c == null) continue;
				var p = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
				if (p == null) continue;
				var s = p.GetValue(c) as string;
				if (!string.IsNullOrWhiteSpace(s)) return c;
			}
			return null;
		}
		catch { return null; }
	}

	public override void OnApplicationQuit()
	{
		BeginQuit("OnApplicationQuit");
		// 避免「遊戲結束時卡死」：不要在退出流程呼叫 Tolk_Unload/FreeLibrary（某些環境會卡在這裡）。
		// OS 會在行程結束時回收 DLL 與資源。
	}

	private void BeginQuit(string reason)
	{
		try
		{
			if (_isQuitting) return;
			_isQuitting = true;
			try { _speechEvent.Set(); } catch { }
			try { Patches.AutoSpeakEnabled = false; } catch { }
			MelonLogger.Msg("TolkExporter: BeginQuit(" + reason + ")");
		}
		catch { }
	}
}

