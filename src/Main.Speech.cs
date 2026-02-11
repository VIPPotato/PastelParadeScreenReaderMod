using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using MelonLoader;

namespace PastelParadeAccess;

public partial class Main
{
private void InitTolk()
	{
		if (tolkInitialized || _isQuitting) return;
		try
		{
			var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
			var tolkDir = Path.Combine(baseDir, "tolk");
			var tolkDll = Path.Combine(tolkDir, "Tolk.dll");
			var arch = (IntPtr.Size == 8) ? "x64" : "x86";
			var libDir = Path.Combine(tolkDir, "libs", arch);

			MelonLogger.Msg("TolkExporter: InitTolk start. baseDir=" + baseDir + " arch=" + arch);

			try
			{
				// 讓 driver DLL 可以被找到（NVDA/Jaws 等後端都在這裡）
				if (Directory.Exists(libDir))
				{
					SetDllDirectoryW(libDir);
					MelonLogger.Msg("TolkExporter: SetDllDirectory libs=" + libDir);
				}
				// 注意：SetDllDirectory 只會設定「單一」目錄，後一次呼叫會覆蓋前一次。
				// 因為 driver DLL 在 libs\\x64（或 x86），所以這裡不要再把目錄改成 tolkDir，
				// Tolk.dll 本體我們用完整路徑 LoadLibraryW 載入即可。
			}
			catch { }

			// 主動用完整路徑載入一次，避免 DllImport("Tolk.dll") 找不到（因為它不在遊戲根目錄）。
			try
			{
				if (File.Exists(tolkDll))
				{
					nativeLibraryHandle = LoadLibraryW(tolkDll);
					if (nativeLibraryHandle == IntPtr.Zero)
					{
						MelonLogger.Warning("TolkExporter: LoadLibraryW failed. path=" + tolkDll + " err=" + Marshal.GetLastWin32Error());
					}
					else
					{
						MelonLogger.Msg("TolkExporter: LoadLibraryW ok. path=" + tolkDll);
					}
				}
				else
				{
					MelonLogger.Warning("TolkExporter: Tolk.dll not found at " + tolkDll);
				}
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("TolkExporter: LoadLibraryW exception: " + ex);
			}

			Tolk_Load();
			// 注意：不同版本的 Tolk.dll 不一定包含 Tolk_HasSpeech/TrySAPI/PreferSAPI 這些 export。
			// 我們不再依賴這些判斷來「阻擋輸出」，最多只做嘗試性啟用/紀錄。
			try { Tolk_TrySAPI(true); } catch { }
			try { Tolk_PreferSAPI(false); } catch { }
			try
			{
				var p = Tolk_DetectScreenReader();
				var sr = (p == IntPtr.Zero) ? "" : (Marshal.PtrToStringUni(p) ?? "");
				MelonLogger.Msg("TolkExporter: Tolk_Load ok. ScreenReader=" + sr);
			}
			catch
			{
				MelonLogger.Msg("TolkExporter: Tolk_Load ok.");
			}

			tolkInitialized = true;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: Tolk_Load failed: " + ex);
		}
	}

	internal void SendToTolk(string text)
	{
		try
		{
			if (_isQuitting) return;
			if (string.IsNullOrWhiteSpace(text)) return;

			text = NormalizeOutputText(text);
			if (string.IsNullOrEmpty(text)) return;

			var now = DateTime.Now;

			// suppress window 期間，允許「非互動文字」（標題/載入提示） bypass，避免被 exporter 的抑制吃掉
			//（注意：這個 suppress window 是由 selection 朗讀設的；我們只放行看起來像標題/提示的文字）
			if (now < _suppressTextSendsUntil && LooksLikeHeaderOrTip(text))
			{
				_lastSentText = text;
				_lastSentAt = now;
				LogSpeakDebug(text);
				EnqueueSpeech(text, priority: true);
				return;
			}

			// 去重：避免 UI 在短時間內重複丟同句導致狂刷，但不能造成「調整設定要等 1 秒」的體感延遲
			if (text == _lastSentText && (now - _lastSentAt).TotalMilliseconds < 250.0)
			{
				// 啟動畫面標題/載入提示常會在很短時間內連續 set_text（甚至被不同 hook 重複送出），
				// 這裡對「看起來像標題/提示」的字串放行一次，避免 0 次朗讀。
				if (!LooksLikeHeaderOrTip(text))
					return;
				LogSpeakDebug(text);
				EnqueueSpeech(text, priority: true);
				_lastSentAt = now;
				return;
			}

			_lastSentText = text;
			_lastSentAt = now;
			LogSpeakDebug(text);
			EnqueueSpeech(text, priority: false);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("TolkExporter: SendToTolk failed: " + ex);
		}
	}

	internal void SendToTolkPriority(string text)
	{
		try
		{
			if (_isQuitting) return;
			if (string.IsNullOrWhiteSpace(text)) return;
			text = NormalizeOutputText(text);
			if (string.IsNullOrEmpty(text)) return;

			LogSpeakDebug(text);
			// priority：清空佇列，確保「關鍵非互動文字」不會被排隊/抑制吃掉
			EnqueueSpeech(text, priority: true);
		}
		catch { }
	}

	// 對話框式「正文」：只唸一次、priority、避免被 suppress/dedupe 吃掉
	// 用途：某些非互動提示文字（例如 calibration/Adjust Timing 的提示）在本版 UI 流程下很容易被一般抑制邏輯吞掉。
	internal void SpeakDialogBodyOnce(string bodyText)
	{
		try
		{
			if (_isQuitting) return;
			bodyText = NormalizeOutputText(bodyText);
			if (string.IsNullOrWhiteSpace(bodyText)) return;

			var now = DateTime.Now;
			// reuse dialog de-dupe fields; keep it short-lived
			if (string.Equals(_dialogBundleText, bodyText, StringComparison.Ordinal) && (now - _dialogBundleSpokenAt).TotalMilliseconds < 1200)
				return;

			_dialogRootInstanceId = -1; // sentinel; real dialogs use a non-zero instance id
			_dialogBundleText = bodyText;
			_dialogBundleSpokenAt = now;
			_dialogContextUntil = now.AddSeconds(4);

			SendToTolkPriority(bodyText);
		}
		catch { }
	}

	// For TMP-driven texts that stabilize a bit later
	internal void SpeakDialogBodyOnceDelayed(string bodyText, int delayMs)
	{
		try
		{
			if (_isQuitting) return;
			bodyText = NormalizeOutputText(bodyText);
			if (string.IsNullOrWhiteSpace(bodyText)) return;
			if (delayMs < 0) delayMs = 0;
			if (delayMs > 5000) delayMs = 5000;

			var at = DateTime.Now.AddMilliseconds(delayMs);
			// de-dupe pending items
			for (int i = 0; i < _delayedDialogBodies.Count; i++)
			{
				if (string.Equals(_delayedDialogBodies[i].text, bodyText, StringComparison.Ordinal))
				{
					_delayedDialogBodies[i] = (at, bodyText);
					return;
				}
			}
			_delayedDialogBodies.Add((at, bodyText));
		}
		catch { }
	}

	private void LogSpeakDebug(string text)
	{
		// Debug 用：把「實際送去朗讀」的字串寫進 Latest.log，方便你驗證 UI/清單朗讀內容
		try
		{
			if (_isQuitting) return;
			if (string.IsNullOrWhiteSpace(text)) return;

			var now = DateTime.Now;
			// 去重/節流：避免拖曳 slider 或重複事件把 log 洗爆
			if (string.Equals(_lastLoggedSpeak, text, StringComparison.Ordinal) && (now - _lastLoggedSpeakAt).TotalMilliseconds < 250)
				return;

			_lastLoggedSpeak = text;
			_lastLoggedSpeakAt = now;
			MelonLogger.Msg("[Tolk Exporter] Speak: " + text);
		}
		catch { }
	}

	private static bool LooksLikeHeaderOrTip(string text)
	{
		try
		{
			text = NormalizeOutputText(text);
			if (string.IsNullOrWhiteSpace(text)) return false;

			// value-like / noisy
			if (text.StartsWith("ver", StringComparison.OrdinalIgnoreCase)) return false;
			if (text.Contains("%") || text.Contains("ms") || text.StartsWith("x ", StringComparison.OrdinalIgnoreCase) || text.Contains(" x "))
				return false;
			if (text.Length < 2 || text.Length > 120) return false;

			// numeric-only / numeric-ish
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

	internal void RegisterKnownSettingsToggles(object stateInstance)
	{
		try
		{
			if (_isQuitting) return;
			if (stateInstance == null) return;

			var tn = stateInstance.GetType().FullName ?? "";
			var ids = new List<int>();

			// 依遊戲原始碼欄位名取出 MornUGUIButton
			void AddBtnField(string fieldName)
			{
				try
				{
					var f = stateInstance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					var btn = f?.GetValue(stateInstance);
					if (btn == null) return;
					var m = btn.GetType().GetMethod("GetInstanceID", BindingFlags.Instance | BindingFlags.Public);
					if (m == null) return;
					int id = (int)m.Invoke(btn, null);
					if (id != 0) ids.Add(id);
				}
				catch { }
			}

			if (tn == "PastelParade.UIDisplaySettings")
			{
				AddBtnField("_fullScreenButton");
				AddBtnField("_vsyncButton");
				AddBtnField("_antiAliasingButton");
				AddBtnField("_vibrationButton");
			}
			else if (tn == "PastelParade.UISoundSettingsState")
			{
				AddBtnField("_inputResultButton");
			}
			else
			{
				return;
			}

			if (ids.Count == 0) return;
			lock (_knownSettingsToggleIds)
			{
				foreach (var id in ids) _knownSettingsToggleIds.Add(id);
			}
		}
		catch { }
	}

	internal void ClearKnownSettingsToggles()
	{
		try
		{
			lock (_knownSettingsToggleIds) _knownSettingsToggleIds.Clear();
		}
		catch { }
	}

	private void EnqueueSpeech(string text, bool priority)
	{
		if (_isQuitting) return;
		try
		{
			const int maxQueue = 6;
			if (priority)
			{
				while (_speechQueue.TryDequeue(out _)) { }
				_speechQueueCount = 0;
			}
			else
			{
				while (_speechQueueCount >= maxQueue && _speechQueue.TryDequeue(out _))
				{
					Interlocked.Decrement(ref _speechQueueCount);
				}
			}

			_speechQueue.Enqueue(text);
			Interlocked.Increment(ref _speechQueueCount);
			try { _speechEvent.Set(); } catch { }
		}
		catch { }
	}

	private void SpeechWorkerLoop()
	{
		while (!_isQuitting)
		{
			try
			{
				if (!_speechQueue.TryDequeue(out var text) || string.IsNullOrWhiteSpace(text))
				{
					try { _speechEvent.WaitOne(60); } catch { }
					continue;
				}
				Interlocked.Decrement(ref _speechQueueCount);

				if (_isQuitting) break;
				if (!tolkInitialized) InitTolk();

				// 不強制 interrupt：交給使用者/讀屏軟體自行處理
				bool ok = false;
				try { ok = Tolk_Output(text, false); } catch { ok = false; }
				if (!ok)
				{
					try
					{
						var now = DateTime.Now;
						// 節流：避免失敗時刷爆 log
						if ((now - _lastTolkOutputFailAt).TotalMilliseconds > 1500 || !string.Equals(_lastTolkOutputFailText, text, StringComparison.Ordinal))
						{
							_lastTolkOutputFailAt = now;
							_lastTolkOutputFailText = text;
							MelonLogger.Warning("TolkExporter: Tolk_Output failed, falling back to COM SAPI. text=" + text);
						}
					}
					catch { }
					// 失敗就直接 fallback，避免阻塞主執行緒造成 UI 延遲
					TrySpeakViaComSapi(text);
				}
			}
			catch { }
		}
	}

	private void TrySpeakViaComSapi(string text)
	{
		if (_isQuitting) return;
		if (_sapiFailed) return;
		try
		{
			if (_sapiVoice == null)
			{
				var t = Type.GetTypeFromProgID("SAPI.SpVoice");
				if (t == null) { _sapiFailed = true; return; }
				_sapiVoice = Activator.CreateInstance(t);
			}

			// flags=1 => SVSFlagsAsync（非同步，避免卡住遊戲）
			_sapiVoice.GetType().InvokeMember(
				"Speak",
				BindingFlags.InvokeMethod,
				null,
				_sapiVoice,
				new object[] { text, 1 }
			);
		}
		catch (Exception ex)
		{
			_sapiFailed = true;
			MelonLogger.Warning("TolkExporter: COM SAPI fallback failed: " + ex);
		}
	}

	private static string NormalizeOutputText(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return "";
		s = s.Replace("\r", " ").Replace("\n", " ").Trim();
		while (s.Contains("  ")) s = s.Replace("  ", " ");

		// 最常見的狀況：同一個詞被組成「X X」
		var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 2 && string.Equals(parts[0], parts[1], StringComparison.OrdinalIgnoreCase))
			return parts[0];

		// 一般化：連續重複的 token 壓成一個（避免「返回 返回 返回」）
		var list = new List<string>(parts.Length);
		string last = null;
		for (int i = 0; i < parts.Length; i++)
		{
			var p = parts[i];
			if (string.IsNullOrEmpty(p)) continue;
			if (last != null && string.Equals(last, p, StringComparison.OrdinalIgnoreCase)) continue;
			list.Add(p);
			last = p;
		}
		return list.Count == 0 ? "" : string.Join(" ", list);
	}

	}
