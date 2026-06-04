using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Characters;
using static Characters.Stat;
using Characters.Abilities;
using Characters.Gear;
using Characters.Gear.Items;
using Characters.Gear.Quintessences;
using Characters.Gear.Synergy;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Upgrades;
using Characters.Gear.Weapons;
using Characters.Player;
using Data;
using GameResources;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace ChzzkSkul;

public class ChzzkGameMode : MonoBehaviour
{
	private struct CommandInfo
	{
		public string nickname;

		public string command;

		public bool isStreamer;
	}

	private struct DonationInfo
	{
		public string nickname;

		public int amount;
	}

	private struct ChatMessageInfo
	{
		public string msg;

		public float expireTime;
	}

	public enum VoteState
	{
		Inactive,
		Voting,
		Processing
	}

	public class VoteOption
	{
		public string Title;

		public int Votes;

		public Action<string> Action;
	}

	private const float BUFF_DURATION = 30f;

	private const float HEAL_PERCENT = 0.1f;

	private readonly ConcurrentQueue<CommandInfo> _commandQueue = new ConcurrentQueue<CommandInfo>();

	private readonly ConcurrentQueue<DonationInfo> _donationQueue = new ConcurrentQueue<DonationInfo>();

	private readonly ConcurrentQueue<string> _pendingChats = new ConcurrentQueue<string>();

	private readonly Queue<ChatMessageInfo> _chatMessages = new Queue<ChatMessageInfo>();

	private float _cooldownTimer = 0f;

	private bool _onCooldown = false;

	private float _lastCooldownMessageTime = 0f;

	private System.Random _random = new System.Random();

	public static bool ForceNextDarkEnemy = false;
	public static Map _darkEnemyTargetMap = null;

	private Map _lastMap;

	private static ChzzkGameMode _instance;

	public static HashSet<Inscription.Key> CustomSuperInscriptions = new HashSet<Inscription.Key>();

	private Map _currentMap;

	private bool _streamerUsedCommandInThisMap = false;

	private VoteState _voteState = VoteState.Inactive;

	private float _voteTimer;

	private float _voteAutoTimer;

	private float _chaosTimer;

	private HashSet<string> _votedUsers = new HashSet<string>();

	private List<VoteOption> _currentVoteOptions = new List<VoteOption>();

	private float GLOBAL_COOLDOWN => Plugin.CommandCooldown.Value;

	private HashSet<string> HealCommands => GetCommands(Plugin.CmdHealString.Value);

	private HashSet<string> BuffCommands => GetCommands(Plugin.CmdBuffCurseString.Value);

	private HashSet<string> ItemCommands => GetCommands(Plugin.CmdItemString.Value);

	private HashSet<string> SkullCommands => GetCommands(Plugin.CmdSkullString.Value);

	private HashSet<string> SynergyCommands => GetCommands(Plugin.CmdSynergyString.Value);

	private HashSet<string> OmenCommands => GetCommands(Plugin.CmdOmenString.Value);

	private HashSet<string> BossCommands => GetCommands(Plugin.CmdBossString.Value);

	private HashSet<string> DarkCommands => GetCommands(Plugin.CmdDarkAbilityString.Value);

	private HashSet<string> RandomCommands => GetCommands(Plugin.CmdRandomString.Value);

	private HashSet<string> NpcCommands => GetCommands(Plugin.CmdNpcString.Value);

	private HashSet<string> FoodCommands => GetCommands(Plugin.CmdFoodString.Value);

	private HashSet<string> BoneCommands => GetCommands(Plugin.CmdBoneString.Value);

	private HashSet<string> GoldCommands => GetCommands(Plugin.CmdGoldString.Value);

	private HashSet<string> DarkQuartzCommands => GetCommands(Plugin.CmdDarkQuartzString.Value);

	private HashSet<string> QuintessenceCommands => GetCommands(Plugin.CmdQuintessenceString.Value);

	private HashSet<string> FragmentCommands => GetCommands(Plugin.CmdFragmentString.Value);

	private HashSet<string> DefenseCommands => GetCommands(Plugin.CmdDefenseString.Value);

	private HashSet<string> RandomStatCommands => GetCommands(Plugin.CmdRandomStatString.Value);

	private HashSet<string> GetCommands(string config)
	{
		HashSet<string> hashSet = new HashSet<string>();
		if (config == null)
		{
			return hashSet;
		}
		string[] array = config.Split(',');
		foreach (string text in array)
		{
			string text2 = text.Trim().ToLower();
			if (!string.IsNullOrEmpty(text2))
			{
				hashSet.Add(text2);
			}
		}
		return hashSet;
	}

	private void Awake()
	{
		if ((UnityEngine.Object)(object)_instance != (UnityEngine.Object)null && (UnityEngine.Object)(object)_instance != (UnityEngine.Object)(object)this)
		{
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)this).gameObject);
			return;
		}
		_instance = this;
		UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)(object)((Component)this).gameObject);
	}

	private void Update()
	{
		Map instance = Map.Instance;
		if ((UnityEngine.Object)(object)instance != (UnityEngine.Object)null && (UnityEngine.Object)(object)instance != (UnityEngine.Object)(object)_currentMap)
		{
			_currentMap = instance;
			_streamerUsedCommandInThisMap = false;
		}
		string result;
		while (_pendingChats.TryDequeue(out result))
		{
			_chatMessages.Enqueue(new ChatMessageInfo
			{
				msg = result,
				expireTime = Time.unscaledTime + 5f
			});
			if (_chatMessages.Count > 6)
			{
				_chatMessages.Dequeue();
			}
		}
		while (_chatMessages.Count > 0 && Time.unscaledTime > _chatMessages.Peek().expireTime)
		{
			_chatMessages.Dequeue();
		}
		if ((UnityEngine.Object)(object)Map.Instance != (UnityEngine.Object)null && (UnityEngine.Object)(object)_lastMap != (UnityEngine.Object)(object)Map.Instance)
		{
			_lastMap = Map.Instance;
			if (Plugin.EnableVote.Value && Plugin.VoteTriggerMode.Value.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0 && _voteState == VoteState.Inactive)
			{
				StartVote();
			}
			if (ForceNextDarkEnemy)
			{
				if (_darkEnemyTargetMap == null)
				{
					// 지금 막 진입한 이 맵이 타겟 맵
					_darkEnemyTargetMap = Map.Instance;
					Debug.Log((object)"[ChzzkGameMode] 다음 맵 커스텀 몬스터 강화 적용 완료 (타겟 맵 지정)");
					ShowFloatingText("어둠의 기운이 다음 맵을 뒤덮습니다... (검은 적 등장!)");
				}
				else if ((UnityEngine.Object)(object)_darkEnemyTargetMap != (UnityEngine.Object)(object)Map.Instance)
				{
					// 타겟 맵을 지나 다음 맵으로 넘어왔으므로 효과 종료
					ForceNextDarkEnemy = false;
					_darkEnemyTargetMap = null;
				}
			}
		}
		if (Plugin.EnableVote.Value)
		{
			if (_voteState == VoteState.Inactive && Plugin.VoteTriggerMode.Value.IndexOf("Timer", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				_voteAutoTimer += Time.unscaledDeltaTime;
				if (_voteAutoTimer >= Plugin.VoteIntervalSeconds.Value)
				{
					StartVote();
					_voteAutoTimer = 0f;
				}
			}
			else if (_voteState == VoteState.Voting)
			{
				_voteTimer -= Time.unscaledDeltaTime;
				if (_voteTimer <= 0f)
				{
					EndVote();
				}
			}
		}

		if (Plugin.EnableChaosMode != null && Plugin.EnableChaosMode.Value)
		{
			if (Plugin.ChaosIntervalSeconds != null)
			{
				_chaosTimer += Time.unscaledDeltaTime;
				if (_chaosTimer >= Plugin.ChaosIntervalSeconds.Value)
				{
					_chaosTimer = 0f;
					try
					{
						TriggerRandomChaosCommand();
					}
					catch (Exception ex)
					{
						Debug.LogError($"[ChaosMode] 오류: {ex}");
					}
				}
			}
		}

		if (_onCooldown)
		{
			_cooldownTimer -= Time.unscaledDeltaTime;
			if (_cooldownTimer <= 0f)
			{
				_onCooldown = false;
				_cooldownTimer = 0f;
			}
		}
		DonationInfo result2;
		while (_donationQueue.TryDequeue(out result2))
		{
			try
			{
				HandleDonation(result2.nickname, result2.amount);
			}
			catch (Exception arg)
			{
				Debug.LogError((object)$"[ChzzkGameMode] 후원 실행 오류: {arg}");
			}
		}
		CommandInfo result3;
		while (_commandQueue.TryDequeue(out result3))
		{
			string nickname = result3.nickname;
			string command = result3.command;
			if (!result3.isStreamer && _onCooldown)
			{
				if (Time.unscaledTime - _lastCooldownMessageTime > 1f)
				{
					_lastCooldownMessageTime = Time.unscaledTime;
					ShowFloatingText($"명령어 쿨타임.. ({_cooldownTimer:F1}초 남음)");
				}
				continue;
			}
			if (!result3.isStreamer)
			{
				_onCooldown = true;
				_cooldownTimer = GLOBAL_COOLDOWN;
			}
			try
			{
				if (RandomCommands.Contains(command))
				{
					DoRandom(nickname);
				}
				else if (HealCommands.Contains(command) && Plugin.AllowHeal.Value)
				{
					DoHeal(nickname);
				}
				else if (NpcCommands.Contains(command) && Plugin.AllowNpc.Value)
				{
					DoNPC(nickname);
				}
				else if (FoodCommands.Contains(command) && Plugin.AllowFood.Value)
				{
					DoFood(nickname);
				}
				else if (BoneCommands.Contains(command) && Plugin.AllowBone.Value)
				{
					DoBone(nickname);
				}
				else if (GoldCommands.Contains(command) && Plugin.AllowGold.Value)
				{
					DoGold(nickname);
				}
				else if (DarkQuartzCommands.Contains(command) && Plugin.AllowDarkQuartz.Value)
				{
					DoDarkQuartz(nickname);
				}
				else if (QuintessenceCommands.Contains(command) && Plugin.AllowQuintessence.Value)
				{
					((UnityEngine.MonoBehaviour)this).StartCoroutine(DoQuintessenceCoroutine(nickname));
				}
				else if (FragmentCommands.Contains(command) && Plugin.AllowFragment.Value)
				{
					((UnityEngine.MonoBehaviour)this).StartCoroutine(DoFragmentCoroutine(nickname));
				}
				else if (DefenseCommands.Contains(command) && Plugin.AllowDefense.Value)
				{
					DoDefense(nickname);
				}
				else if (BuffCommands.Contains(command) && Plugin.AllowBuffCurse.Value)
				{
					DoBuffOrCurse(nickname);
				}
				else if (ItemCommands.Contains(command) && Plugin.AllowItem.Value)
				{
					((MonoBehaviour)this).StartCoroutine(DoItemCoroutine(nickname));
				}
				else if (SkullCommands.Contains(command) && Plugin.AllowSkull.Value)
				{
					((MonoBehaviour)this).StartCoroutine(DoSkullCoroutine(nickname));
				}
				else if (SynergyCommands.Contains(command) && Plugin.AllowSynergy.Value)
				{
					DoSynergy(nickname);
				}
				else if (OmenCommands.Contains(command) && Plugin.AllowOmen.Value)
				{
					DoOmen(nickname);
				}
				else if (BossCommands.Contains(command) && Plugin.AllowBoss.Value)
				{
					DoBoss(nickname);
				}
				else if (DarkCommands.Contains(command) && Plugin.AllowDarkAbility.Value)
				{
					DoDarkAbility(nickname);
				}
				else if (RandomStatCommands.Contains(command) && Plugin.AllowRandomStat.Value)
				{
					DoRandomStat(nickname);
				}
			}
			catch (Exception arg2)
			{
				Debug.LogError((object)$"[ChzzkGameMode] 커맨드 실행 오류: {arg2}");
			}
		}
	}

	private void TriggerRandomChaosCommand()
	{
		List<Action<string>> chaosActions = new List<Action<string>>()
		{
			(n) => DoHeal(n),
			(n) => DoBuffOrCurse(n),
			(n) => { ((UnityEngine.MonoBehaviour)this).StartCoroutine(DoItemCoroutine(n)); },
			(n) => { ((UnityEngine.MonoBehaviour)this).StartCoroutine(DoSkullCoroutine(n)); },
			(n) => DoSynergy(n),
			(n) => DoOmen(n),
			(n) => DoBoss(n),
			(n) => DoDarkAbility(n),
			(n) => DoRandomStat(n)
		};
		chaosActions[_random.Next(chaosActions.Count)]("$CHAOS$");
	}

	private void OnGUI()
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Expected O, but got Unknown
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Expected O, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Expected O, but got Unknown
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0231: Expected O, but got Unknown
		//IL_0278: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Unknown result type (might be due to invalid IL or missing references)
		if (_chatMessages.Count == 0 && (!_onCooldown || !(_cooldownTimer > 0f)) && _voteState != VoteState.Voting)
		{
			return;
		}
		GUIStyle val = new GUIStyle();
		if ((UnityEngine.Object)(object)GUI.skin != (UnityEngine.Object)null && GUI.skin.label != null)
		{
			val = new GUIStyle(GUI.skin.label);
		}
		val.fontSize = 24;
		val.wordWrap = true;
		val.richText = true;
		val.normal.textColor = Color.white;
		GUIStyle val2 = new GUIStyle(val);
		val2.normal.textColor = Color.black;
		float num = (float)Screen.height - 350f;
		Rect val3 = default(Rect);
		foreach (ChatMessageInfo chatMessage in _chatMessages)
		{
			val3 = new Rect(20f, num, 800f, 50f);
			Rect val4 = new Rect(val3.x + 1f, val3.y + 1f, val3.width, val3.height);
			GUI.Label(val4, chatMessage.msg, val2);
			val4 = new Rect(val3.x - 1f, val3.y - 1f, val3.width, val3.height);
			GUI.Label(val4, chatMessage.msg, val2);
			GUI.Label(val3, chatMessage.msg, val);
			num += 40f;
		}
		GUIStyle val5 = new GUIStyle(val);
		val5.alignment = (TextAnchor)2;
		Rect rect = default(Rect);
		rect = new Rect((float)(Screen.width - 320), 20f, 300f, 50f);
		string text = ((_onCooldown && _cooldownTimer > 0f) ? $"<color=#FF5555>커맨드 쿨타임: {_cooldownTimer:F1}초</color>" : "<color=#55FF55>커맨드 사용 가능 \ud83d\udfe2</color>");
		DrawOutlineText(rect, text, val5, val2);
		if (_voteState == VoteState.Voting)
		{
			GUIStyle val6 = new GUIStyle(val);
			val6.fontSize = 28;
			val6.alignment = (TextAnchor)0;
			float num2 = 50f;
			float num3 = 100f;
			string text2 = $"<color=#FFFF00>[시청자 투표 진행중!] 남은 시간: {_voteTimer:F1}초</color>\n<size=22>채팅창에 번호(1, 2, 3)를 입력해 투표하세요!</size>";
			DrawOutlineText(new Rect(num2, num3, 600f, 100f), text2, val6, val2);
			num3 += 80f;
			for (int i = 0; i < _currentVoteOptions.Count; i++)
			{
				string text3 = $"{i + 1}. {_currentVoteOptions[i].Title}  <color=#AAAAFF>[{_currentVoteOptions[i].Votes}표]</color>";
				DrawOutlineText(new Rect(num2, num3, 600f, 50f), text3, val6, val2);
				num3 += 40f;
			}
		}
	}

	private void DrawOutlineText(Rect rect, string text, GUIStyle style, GUIStyle outlineStyle)
	{
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
		string text2 = text.Replace("<color=#FF5555>", "").Replace("<color=#55FF55>", "").Replace("<color=#FFFF00>", "")
			.Replace("<color=#AAAAFF>", "")
			.Replace("</color>", "")
			.Replace("<size=22>", "")
			.Replace("</size>", "");
		Rect val = rect;
		GUI.Label(new Rect(val.x + 1f, val.y + 1f, val.width, val.height), text2, outlineStyle);
		GUI.Label(new Rect(val.x - 1f, val.y - 1f, val.width, val.height), text2, outlineStyle);
		GUI.Label(rect, text, style);
	}

	private bool IsValidCommand(string text)
	{
		return HealCommands.Contains(text) || BuffCommands.Contains(text) || ItemCommands.Contains(text) || SkullCommands.Contains(text) || SynergyCommands.Contains(text) || OmenCommands.Contains(text) || BossCommands.Contains(text) || DarkCommands.Contains(text) || RandomCommands.Contains(text) || RandomStatCommands.Contains(text) || NpcCommands.Contains(text) || FoodCommands.Contains(text) || BoneCommands.Contains(text) || GoldCommands.Contains(text) || DarkQuartzCommands.Contains(text) || QuintessenceCommands.Contains(text) || FragmentCommands.Contains(text) || DefenseCommands.Contains(text);
	}

	public void OnChatMessage(string nickname, string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}
		string text = message.Trim().ToLower();
		nickname = (string.IsNullOrEmpty(nickname) ? "익명" : nickname);
		
		bool isCommand = Plugin.EnableChatCommands != null && Plugin.EnableChatCommands.Value && IsValidCommand(text);

		if (_voteState == VoteState.Voting && int.TryParse(text.Replace("!", ""), out var result) && result >= 1 && result <= _currentVoteOptions.Count && !_votedUsers.Contains(nickname))
		{
			_votedUsers.Add(nickname);
			_currentVoteOptions[result - 1].Votes++;
		}
		else if (isCommand)
		{
			if (nickname == Plugin.StreamerNickname.Value)
			{
				if (Plugin.AllowStreamerCommand.Value)
				{
					if (_streamerUsedCommandInThisMap)
					{
						ShowFloatingText("스트리머는 이번 맵에서 이미 명령어를 사용했습니다!");
						return;
					}
					_streamerUsedCommandInThisMap = true;
					_commandQueue.Enqueue(new CommandInfo
					{
						nickname = nickname,
						command = text,
						isStreamer = true
					});
				}
			}
			else
			{
				_commandQueue.Enqueue(new CommandInfo
				{
					nickname = nickname,
					command = text,
					isStreamer = false
				});
			}
		}
		else
		{
			string text2 = nickname.Replace("<", "").Replace(">", "");
			_pendingChats.Enqueue("<color=#00FF00>[" + text2 + "]</color> " + message);
		}
	}

	public void OnDonation(string nickname, int amount)
	{
		nickname = (string.IsNullOrEmpty(nickname) ? "익명" : nickname);
		_donationQueue.Enqueue(new DonationInfo
		{
			nickname = nickname,
			amount = amount
		});
	}

	private void HandleDonation(string nickname, int amount)
	{
		if (amount >= 700000)
		{
			DoDonationNukeAll(nickname);
		}
		else if (amount >= 500000)
		{
			DoDonationNukeItems(nickname);
		}
		else if (amount >= 150000)
		{
			DoDonationSubSkullDelete(nickname);
		}
		else if (amount >= 100000)
		{
			DoDonationDarkAbilityDelete(nickname);
		}
		else if (amount >= 50000)
		{
			DoDonationItemDelete(nickname);
		}
		else if (amount >= 20000)
		{
			DoDonationSynergy(nickname);
		}
		else if (amount >= 10000)
		{
			if (UnityEngine.Random.value > 0.5f)
			{
				DoDonationDarkAbility(nickname);
			}
			else
			{
				((UnityEngine.MonoBehaviour)this).StartCoroutine(DoFragmentCoroutine(nickname));
			}
		}
		else if (amount >= 5000)
		{
			((MonoBehaviour)this).StartCoroutine(DoDonationItemCoroutine(nickname));
		}
		else if (amount >= 1000)
		{
			DoDonationStat(nickname);
		}
	}

	private void DoDonationStat(string nickname)
	{
		DoGold(nickname);
		DoDarkQuartz(nickname);
		DoBone(nickname);
	}

	private void DoBone(string nickname)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		int num = _random.Next(5, 15);
		Service service = GetService();
		if (service != null)
		{
			LevelManager levelManager = service.levelManager;
			if (levelManager != null)
			{
				levelManager.DropCurrency(GameData.Currency.Type.Bone, num, num, ((Component)player).transform.position + Vector3.up * 1.5f);
			}
		}
		ShowFloatingText($"{nickname}님이 뼈 파편 {num}개를 떨어뜨렸습니다!");
	}

	private void DoGold(string nickname)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		int num = _random.Next(300, 1000);
		Service service = GetService();
		if (service != null)
		{
			LevelManager levelManager = service.levelManager;
			if (levelManager != null)
			{
				levelManager.DropCurrency(GameData.Currency.Type.Gold, num, 10, ((Component)player).transform.position + Vector3.up * 1.5f);
			}
		}
		ShowFloatingText($"{nickname}님이 골드 {num}G를 떨어뜨렸습니다!");
	}

	private void DoDarkQuartz(string nickname)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		Service service = GetService();
		if (service != null)
		{
			LevelManager levelManager = service.levelManager;
			if (levelManager != null)
			{
				levelManager.DropCurrency(GameData.Currency.Type.DarkQuartz, 1, 1, ((Component)player).transform.position + Vector3.up * 1.5f);
			}
		}
		ShowFloatingText($"{nickname}님이 검은마석 1개를 떨어뜨렸습니다!");
	}

	private void DoFood(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		
		Characters.Abilities.AbilityBuff[] buffs = UnityEngine.Resources.FindObjectsOfTypeAll<Characters.Abilities.AbilityBuff>();
		if (buffs != null && buffs.Length > 0)
		{
			Characters.Abilities.AbilityBuff chosen = buffs[_random.Next(buffs.Length)];
			Characters.Abilities.AbilityBuff spawnedFood = UnityEngine.Object.Instantiate<Characters.Abilities.AbilityBuff>(chosen, ((Component)player).transform.position, Quaternion.identity);
			spawnedFood.price = 0;
			spawnedFood.Initialize();
			((Component)spawnedFood).gameObject.SetActive(true);
			ShowFloatingText(nickname + "님이 상점 음식(" + spawnedFood.displayName + ")을 소환했어요!");
			return;
		}
		
		double num = ((Health)player.health).PercentHeal(0.2f);
		ShowFloatingText(nickname + "님이 음식을 선물했어요! (체력 회복)");
	}

	private void DoNPC(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		try
		{
			Level.Npc.FieldNpcs.FieldNpc[] npcs = UnityEngine.Resources.FindObjectsOfTypeAll<Level.Npc.FieldNpcs.FieldNpc>();
			if (npcs != null && npcs.Length > 0)
			{
				Level.Npc.FieldNpcs.FieldNpc chosenNpc = npcs[_random.Next(npcs.Length)];
				Vector3 spawnPos = ((Component)player).transform.position + Vector3.right * 2f;
				Level.Npc.FieldNpcs.FieldNpc spawnedNpc = UnityEngine.Object.Instantiate<Level.Npc.FieldNpcs.FieldNpc>(chosenNpc, spawnPos, Quaternion.identity);
				if ((UnityEngine.Object)(object)Level.Map.Instance != (UnityEngine.Object)null)
				{
					((Component)spawnedNpc).transform.SetParent(((Component)Level.Map.Instance).transform, true);
				}
				((Component)spawnedNpc).gameObject.SetActive(true);
				
				// 강제로 상호작용 활성화
				try
				{
					System.Reflection.MethodInfo method = typeof(Level.Npc.FieldNpcs.FieldNpc).GetMethod("OnCageDestroyed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					if (method != null)
					{
						method.Invoke(spawnedNpc, null);
					}
				}
				catch { }
				
				ShowFloatingText(nickname + "님이 [" + ((object)chosenNpc).GetType().Name + "] NPC를 소환했습니다!");
				return;
			}
			
			// fallback
			ShowFloatingText(nickname + "님이 부른 NPC가 오지 못해서 대신 버프를 남겼습니다!");
			DoBuffOrCurse("유령 NPC");
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[ChzzkGameMode] DoNPC 오류: " + ex));
			ShowFloatingText(nickname + "님이 부른 NPC가 오지 못해서 대신 버프를 남겼습니다!");
			DoBuffOrCurse("유령 NPC");
		}
	}


	private IEnumerator DoDonationItemCoroutine(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			yield break;
		}
		int roll = _random.Next(1, 101);
		if (roll <= 50)
		{
			Inventory inventory = player.playerComponents.inventory;
			if (inventory != null && inventory.item.items.Count > 0)
			{
				int randomIndex = _random.Next(inventory.item.items.Count);
				Item itemToRemove = inventory.item.items[randomIndex];
				inventory.item.Remove(itemToRemove);
				ShowFloatingText(nickname + "님이 아이템을 삭제했습니다! (후원)");
			}
			else
			{
				ShowFloatingText(nickname + "님의 아이템 삭제 시도! 빈 인벤토리네요. (후원)");
			}
			yield break;
		}
		Service service = GetService();
		if ((UnityEngine.Object)(object)service == (UnityEngine.Object)null)
		{
			yield break;
		}
		Rarity rarity = PickWeightedRarity();
		ItemReference itemRef = service.gearManager.GetItemToTake(rarity);
		if (itemRef == null)
		{
			itemRef = service.gearManager.GetItemToTake((Rarity)0);
		}
		if (itemRef != null)
		{
			ItemRequest request = itemRef.LoadAsync();
			while (!((Request<Item>)(object)request).isDone)
			{
				yield return null;
			}
			Item drop = service.levelManager.DropItem(request, ((Component)player).transform.position);
			if (player.playerComponents.inventory.item.items.Count((Item i) => (UnityEngine.Object)(object)i != (UnityEngine.Object)null) < 9)
			{
				player.playerComponents.inventory.item.TryEquip(drop);
				ShowFloatingText(nickname + "님이 아이템을 추가했습니다! (후원)");
			}
			else
			{
				ShowFloatingText(nickname + "님이 아이템을 바닥에 떨궜습니다! (후원)");
			}
		}
	}

	private void DoDonationDarkAbility(string nickname)
	{
		DoDarkAbility(nickname);
	}

	private void DoDonationSynergy(string nickname)
	{
		//IL_0136: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0201: Unknown result type (might be due to invalid IL or missing references)
		//IL_0206: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Invalid comparison between Unknown and I4
		//IL_022e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_026e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		Inventory inventory = player.playerComponents.inventory;
		if (inventory == null || (UnityEngine.Object)(object)inventory.synergy == (UnityEngine.Object)null || inventory.synergy.inscriptions == null)
		{
			return;
		}
		Synergy synergy = inventory.synergy;
		List<Inscription.Key> list = new List<Inscription.Key>();
		List<Inscription.Key> list2 = new List<Inscription.Key>();
		if (Inscription.keys != null)
		{
			foreach (Inscription.Key key in Inscription.keys)
			{
				if ((int)key != 0 && (int)key != 35)
				{
					if (synergy.inscriptions[key].count > 0)
					{
						list.Add(key);
					}
					else
					{
						list2.Add(key);
					}
				}
			}
		}
		int num = _random.Next(1, 101);
		if (num <= 50)
		{
			if (list.Count > 0)
			{
				Inscription.Key val = list[_random.Next(list.Count)];
				if (synergy.inscriptions[val].bonusCount > 0)
				{
					Inscription obj = synergy.inscriptions[val];
					obj.bonusCount--;
					inventory.UpdateSynergy();
					ShowFloatingText($"{nickname}님이 [{val}] 각인을 1 삭제했습니다! (후원)");
				}
				else
				{
					ShowFloatingText(nickname + "님이 각인 삭제를 시도했으나 보너스가 없습니다! (후원)");
				}
			}
			else
			{
				ShowFloatingText(nickname + "님의 각인 삭제가 불발되었습니다! (후원)");
			}
			return;
		}
		Inscription.Key val2;
		if (list.Count > 0 && _random.Next(1, 101) <= 50)
		{
			val2 = list[_random.Next(list.Count)];
		}
		else
		{
			if (list2.Count <= 0)
			{
				return;
			}
			val2 = list2[_random.Next(list2.Count)];
		}
		Inscription obj2 = synergy.inscriptions[val2];
		obj2.bonusCount++;
		CustomSuperInscriptions.Add(val2);
		inventory.UpdateSynergy();
		ShowFloatingText($"{nickname}님이 [{val2}] 각인을 1 추가했습니다! (후원)");
	}

	private void DoDonationItemDelete(string nickname)
	{
		Character player = GetPlayer();
		if (!((UnityEngine.Object)(object)player == (UnityEngine.Object)null))
		{
			Inventory inventory = player.playerComponents.inventory;
			if (inventory != null && inventory.item.items.Count > 0)
			{
				int index = _random.Next(inventory.item.items.Count);
				Item val = inventory.item.items[index];
				inventory.item.Remove(val);
				ShowFloatingText(nickname + "님이 아이템 1개를 확정 삭제했습니다! (후원)");
			}
		}
	}

	private void DoDonationDarkAbilityDelete(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		UpgradeInventory upgrade = player.playerComponents.inventory.upgrade;
		if ((UnityEngine.Object)(object)upgrade == (UnityEngine.Object)null)
		{
			return;
		}
		List<UpgradeObject> upgrades = upgrade.upgrades;
		List<int> list = new List<int>();
		for (int i = 0; i < upgrades.Count; i++)
		{
			if ((UnityEngine.Object)(object)upgrades[i] != (UnityEngine.Object)null)
			{
				list.Add(i);
			}
		}
		if (list.Count > 0)
		{
			int num = list[_random.Next(list.Count)];
			upgrade.Remove(num);
			upgrade.Trim();
			ShowFloatingText(nickname + "님이 검은 능력을 확정 삭제했습니다! (후원)");
		}
	}

	private bool TryDeleteSubSkull()
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return false;
		}
		WeaponInventory weapon = player.playerComponents.inventory.weapon;
		if ((UnityEngine.Object)(object)weapon == (UnityEngine.Object)null)
		{
			return false;
		}
		try
		{
			Type type = ((object)weapon).GetType();
			FieldInfo fieldInfo = type.GetField("weapons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("_weapons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (fieldInfo != null)
			{
				Array arr = fieldInfo.GetValue(weapon) as Array;
				if (arr != null)
				{
					Weapon polymorphOrCurrent = weapon.polymorphOrCurrent;
					Weapon subSkull = null;
					for (int i = 0; i < arr.Length; i++)
					{
						Weapon val = (Weapon)arr.GetValue(i);
						if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null && (UnityEngine.Object)(object)val != (UnityEngine.Object)(object)polymorphOrCurrent)
						{
							subSkull = val;
							break;
						}
					}
					if (subSkull != null)
					{
						// CharacterAnimationController.animations 리스트에서 삭제할 스컬의 애니메이션 미리 제거
						if (player.animationController != null)
						{
							FieldInfo animsField = player.animationController.GetType().GetField("animations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (animsField != null)
							{
								System.Collections.IList anims = animsField.GetValue(player.animationController) as System.Collections.IList;
								if (anims != null)
								{
									CharacterAnimation[] subAnims = ((Component)subSkull).GetComponentsInChildren<CharacterAnimation>(true);
									foreach (CharacterAnimation a in subAnims)
									{
										anims.Remove(a);
									}
									// null(또는 파괴된) 레퍼런스들도 싹 정리
									for (int j = anims.Count - 1; j >= 0; j--)
									{
										object animObj = anims[j];
										if (animObj == null || animObj.Equals(null))
										{
											anims.RemoveAt(j);
										}
									}
								}
							}
						}

						arr.SetValue(polymorphOrCurrent, 0);
						for (int i = 1; i < arr.Length; i++)
						{
							arr.SetValue(null, i);
						}

						// ForceEquip이 GetComponentsInChildren을 통해 이 스컬을 다시 찾아내서 
						// animationController에 등록하는 것을 막기 위해 부모를 해제합니다.
						((Component)subSkull).transform.SetParent(null);

						MethodInfo forceEquip = type.GetMethod("ForceEquip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (forceEquip != null)
						{
							forceEquip.Invoke(weapon, new object[1] { polymorphOrCurrent });
						}
						
						((Component)subSkull).gameObject.SetActive(false);
						UnityEngine.Object.Destroy(((Component)subSkull).gameObject);
						return true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("[ChzzkGameMode] 보조 머리 삭제 실패: " + ex));
		}
		return false;
	}

	private void DoDonationSubSkullDelete(string nickname)
	{
		if (TryDeleteSubSkull())
		{
			ShowFloatingText(nickname + "님이 보조 머리를 확정 삭제했습니다! (후원)");
		}
		else
		{
			ShowFloatingText(nickname + "님의 보조 머리 삭제가 불발되었습니다.");
		}
	}

	private void DoDonationNukeItems(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		Inventory inventory = player.playerComponents.inventory;
		if (inventory != null && inventory.item.items.Count > 0)
		{
			for (int num = inventory.item.items.Count - 1; num >= 0; num--)
			{
				inventory.item.Remove(inventory.item.items[num]);
			}
			ShowFloatingText(nickname + "님이 모든 아이템을 파괴했습니다! (후원)");
		}
		else
		{
			ShowFloatingText(nickname + "님이 아이템 파괴를 시도했으나 텅 비었네요!");
		}
	}

	private void DoDonationNukeAll(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		bool flag = false;
		Inventory inventory = player.playerComponents.inventory;
		if (inventory != null && inventory.item.items.Count > 0)
		{
			for (int num = inventory.item.items.Count - 1; num >= 0; num--)
			{
				inventory.item.Remove(inventory.item.items[num]);
			}
			flag = true;
		}
		UpgradeInventory upgrade = player.playerComponents.inventory.upgrade;
		if ((UnityEngine.Object)(object)upgrade != (UnityEngine.Object)null)
		{
			List<UpgradeObject> upgrades = upgrade.upgrades;
			for (int num2 = upgrades.Count - 1; num2 >= 0; num2--)
			{
				if ((UnityEngine.Object)(object)upgrades[num2] != (UnityEngine.Object)null)
				{
					upgrade.Remove(num2);
					flag = true;
				}
			}
			upgrade.Trim();
		}
		if (flag)
		{
			ShowFloatingText(nickname + "님이 모든 아이템과 검은 능력을 증발시켰습니다!! (후원)");
		}
		else
		{
			ShowFloatingText(nickname + "님이 파괴를 시도했으나 이미 아무것도 없네요!");
		}
	}

	private void DoRandom(string nickname)
	{
		int num = _random.Next(1, 4);
		ShowFloatingText($"{nickname}이(가) 랜덤 효과 {num}개를 발동시켰어요!");
		Action<string>[] source = new Action<string>[9]
		{
			delegate(string n)
			{
				DoHeal(n);
			},
			delegate(string n)
			{
				DoBuffOrCurse(n);
			},
			delegate(string n)
			{
				((MonoBehaviour)this).StartCoroutine(DoItemCoroutine(n));
			},
			delegate(string n)
			{
				((MonoBehaviour)this).StartCoroutine(DoSkullCoroutine(n));
			},
			delegate(string n)
			{
				DoSynergy(n);
			},
			delegate(string n)
			{
				DoOmen(n);
			},
			delegate(string n)
			{
				DoBoss(n);
			},
			delegate(string n)
			{
				DoDarkAbility(n);
			},
			delegate(string n)
			{
				DoRandomStat(n);
			}
		};
		List<Action<string>> list = source.OrderBy((Action<string> x) => _random.Next()).Take(num).ToList();
		foreach (Action<string> item in list)
		{
			item(nickname);
		}
	}

	private void DoDefense(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null) return;
		try
		{
			player.stat.AttachValues(new Values((Value[])(object)new Value[1]
			{
				new Value(Category.PercentPoint, Kind.TakingDamage, -0.1)
			}));
			ShowFloatingText(nickname + "이(가) 방어력 10% 증가 축복을 내렸어요!");
		}
		catch
		{
		}
	}

	private void DoRandomStat(string nickname)
	{
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected O, but got Unknown
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Expected O, but got Unknown
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		Kind[] array = (Kind[])(object)new Kind[8]
		{
			Kind.AttackDamage,
			Kind.PhysicalAttackDamage,
			Kind.MagicAttackDamage,
			Kind.AttackSpeed,
			Kind.MovementSpeed,
			Kind.CriticalChance,
			Kind.CriticalDamage,
			Kind.Health
		};
		string[] array2 = new string[8] { "모든 공격력", "물리 공격력", "마법 공격력", "공격 속도", "이동 속도", "치명타 확률", "치명타 데미지", "최대 체력" };
		int num = _random.Next(array.Length);
		Kind val = array[num];
		string text = array2[num];
		bool flag = _random.Next(2) == 0;
		float num2 = (flag ? 0.1f : (-0.1f));
		string text2 = (flag ? "증가" : "감소");
		string text3 = (flag ? "축복" : "저주");
		try
		{
			if (val == Kind.Health)
			{
				double delta = flag ? 30.0 : -30.0;
				if (!flag && player.health.maximumHealth + delta <= 0)
				{
					delta = -(player.health.maximumHealth - 1);
				}
				player.stat.AttachValues(new Values((Value[])(object)new Value[1]
				{
					new Value(Category.Constant, val, delta)
				}));
				ShowFloatingText($"{nickname}이(가) {text3}을 내렸어요! {text} {Math.Abs(delta)} {text2}!");
			}
			else
			{
				player.stat.AttachValues(new Values((Value[])(object)new Value[1]
				{
					new Value(Category.PercentPoint, val, (double)num2)
				}));
				ShowFloatingText(nickname + "이(가) " + text3 + "을 내렸어요! " + text + " 10% " + text2 + "!");
			}
		}
		catch
		{
			ShowFloatingText(nickname + "의 스탯 조작이 빗나갔어요!");
		}
	}

	private void DoHeal(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		int num = _random.Next(1, 101);
		if (num <= 30)
		{
			double currentHealth = ((Health)player.health).currentHealth;
			double maximumHealth = ((Health)player.health).maximumHealth;
			double num2 = maximumHealth * 0.10000000149011612;
			if (currentHealth - num2 < 1.0)
			{
				num2 = currentHealth - 1.0;
			}
			if (num2 > 0.0)
			{
				Type type = ((object)player.health).GetType();
				FieldInfo fieldInfo = type.GetField("_currentHealth", BindingFlags.Instance | BindingFlags.NonPublic) ?? type.GetField("<currentHealth>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
				if (fieldInfo != null)
				{
					fieldInfo.SetValue(player.health, currentHealth - num2);
					ShowFloatingText($"{nickname}이(가) 독약을 선물했어요! -{num2:F0}");
				}
				else
				{
					ShowFloatingText(nickname + "이(가) 독약을 선물했지만 간신히 버텼어요!");
				}
			}
			else
			{
				ShowFloatingText(nickname + "이(가) 독약을 선물했지만 간신히 버텼어요!");
			}
		}
		else
		{
			double num3 = ((Health)player.health).PercentHeal(0.1f);
			ShowFloatingText($"{nickname}이(가) 힐을 선물했어요! +{num3:F0}");
		}
		DoBuffOrCurse(nickname);
	}

	private IEnumerator DoQuintessenceCoroutine(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			yield break;
		}
		Rarity rarity = (Rarity)0;
		Service service = GetService();
		object obj;
		if (service == null)
		{
			obj = null;
		}
		else
		{
			GearManager gearManager = service.gearManager;
			obj = ((gearManager != null) ? gearManager.GetQuintessenceToTake(rarity) : null);
		}
		EssenceReference quintessenceRef = (EssenceReference)obj;
		if (quintessenceRef == null)
		{
			yield break;
		}
		EssenceRequest request = quintessenceRef.LoadAsync();
		while (!((Request<Quintessence>)(object)request).isDone)
		{
			yield return null;
		}
		Service service2 = GetService();
		if (service2 != null)
		{
			LevelManager levelManager = service2.levelManager;
			if (levelManager != null)
			{
				levelManager.DropQuintessence(request, ((Component)player).transform.position + Vector3.up * 1f);
			}
		}
		ShowFloatingText(nickname + "님이 정수를 스폰했습니다!");
	}

	private void DoBuffOrCurse(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		SavableAbilityManager component = ((Component)player).GetComponent<SavableAbilityManager>();
		if (!((UnityEngine.Object)(object)component == (UnityEngine.Object)null))
		{
			int num = _random.Next(1, 101);
			if (num <= 60)
			{
				// BrutalityBuff(8), RageBuff(9), FortitudeBuff(10) 중 하나 선택
				SavableAbilityManager.Name[] buffNames = new SavableAbilityManager.Name[]
				{
					SavableAbilityManager.Name.BrutalityBuff,
					SavableAbilityManager.Name.RageBuff,
					SavableAbilityManager.Name.FortitudeBuff
				};
				string[] buffLabels = new string[] { "잔혹함(공격력↑)", "분노(공격속도↑)", "불굴(방어력↑)" };
				int idx = _random.Next(buffNames.Length);
				SavableAbilityManager.Name chosenBuff = buffNames[idx];
				component.Apply(chosenBuff, 1f);
				ShowFloatingText(nickname + "이(가) " + buffLabels[idx] + " 버프를 선물했어요!");
			}
			else
			{
				component.Apply(SavableAbilityManager.Name.Curse, 1f);
				ShowFloatingText(nickname + "이(가) 신의 은총을 선물해주셨습니다!");
			}
		}
	}

	private IEnumerator DoItemCoroutine(string nickname)
	{
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			yield break;
		}
		int roll10000 = _random.Next(1, 10001);
		if (roll10000 == 1)
		{
			Inventory inventory = player.playerComponents.inventory;
			if (inventory != null && inventory.item.items.Count > 0)
			{
				for (int i = inventory.item.items.Count - 1; i >= 0; i--)
				{
					inventory.item.Remove(inventory.item.items[i]);
				}
				ShowFloatingText(nickname + "이(가) 인벤토리를 텅 비웠어요! (0.01% 기적)");
			}
			yield break;
		}
		int roll10001 = _random.Next(1, 101);
		if (roll10001 <= 40)
		{
			Inventory inventory2 = player.playerComponents.inventory;
			if (inventory2 != null && inventory2.item.items.Count > 0)
			{
				int randomIndex = _random.Next(inventory2.item.items.Count);
				Item itemToRemove = inventory2.item.items[randomIndex];
				inventory2.item.Remove(itemToRemove);
				ShowFloatingText(nickname + "이(가) 인벤토리에서 아이템을 파괴했어요!");
			}
			else
			{
				ShowFloatingText(nickname + "이(가) 아이템 파괴를 시도했으나 텅 비었네요!");
			}
			yield break;
		}
		Service service = GetService();
		if ((UnityEngine.Object)(object)service == (UnityEngine.Object)null)
		{
			yield break;
		}
		Rarity rarity = PickWeightedRarity();
		ItemReference itemRef = null;
		int omenRoll = _random.Next(1, 101);
		if (omenRoll <= 30)
		{
			FieldInfo itemsField = ((object)service.gearManager).GetType().GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic) ?? ((object)service.gearManager).GetType().GetField("items", BindingFlags.Instance | BindingFlags.Public);
			if (itemsField != null && itemsField.GetValue(service.gearManager) is IEnumerable<ItemReference> allItems)
			{
				List<ItemReference> omenItems = allItems.Where((ItemReference val) => (int)val.prefabKeyword1 == 34 || (int)val.prefabKeyword2 == 34).ToList();
				if (omenItems.Count > 0)
				{
					itemRef = omenItems[_random.Next(omenItems.Count)];
				}
			}
		}
		if (itemRef == null)
		{
			itemRef = service.gearManager.GetItemToTake(rarity);
		}
		if (itemRef == null)
		{
			itemRef = service.gearManager.GetItemToTake((Rarity)0);
		}
		if (itemRef != null)
		{
			ItemRequest request = itemRef.LoadAsync();
			while (!((Request<Item>)(object)request).isDone)
			{
				yield return null;
			}
			int randDrop = _random.Next(1, 101);
			Item drop = Singleton<Service>.Instance.levelManager.DropItem(itemRef.LoadAsync(), ((Component)player).transform.position);
			if (randDrop <= 70 && player.playerComponents.inventory.item.items.Count((Item val) => (UnityEngine.Object)(object)val != (UnityEngine.Object)null) < 9)
			{
				player.playerComponents.inventory.item.TryEquip(drop);
				ShowFloatingText(nickname + "이(가) 인벤토리에 아이템을 꽂아줬어요!");
			}
			else if (randDrop <= 70)
			{
				ShowFloatingText(nickname + "이(가) 인벤토리가 꽉차서 아이템을 바닥에 떨궜어요!");
			}
			else
			{
				ShowFloatingText(nickname + "이(가) 아이템을 선물했어요!");
			}
		}
	}

	private IEnumerator DoFragmentCoroutine(string nickname)
	{
		Service service = GetService();
		if ((UnityEngine.Object)(object)service == (UnityEngine.Object)null) yield break;
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null) yield break;

		if (UnityEngine.Random.value < 0.5f)
		{
			DoFragmentDelete(nickname);
			yield break;
		}

		string[] fragments = new string[] {
			"VertebraOfDisbelief",
			"SolitarySternum",
			"RibsOfTerror",
			"TerriblePelvis",
			"FemurOfDespair",
			"JealousTibia",
			"ScapularsOfHatred",
			"HumerusOfFury",
			"ResentfulCollarbone"
		};
		string chosen = fragments[UnityEngine.Random.Range(0, fragments.Length)];
		string targetKey = null;

		foreach (var locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
		{
			foreach (object keyObj in locator.Keys)
			{
				string k = keyObj.ToString();
				if (k.IndexOf(chosen, StringComparison.OrdinalIgnoreCase) >= 0 && k.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
				{
					if (targetKey == null || k.Contains("Gear/"))
					{
						targetKey = k;
					}
				}
			}
		}

		if (targetKey != null)
		{
			var op = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(targetKey);
			yield return op;
			if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
			{
				GameObject prefab = op.Result;
				if ((UnityEngine.Object)(object)prefab != (UnityEngine.Object)null)
				{
					Characters.Gear.Items.Item itemComponent = prefab.GetComponent<Characters.Gear.Items.Item>();
					if ((UnityEngine.Object)(object)itemComponent != (UnityEngine.Object)null)
					{
						service.levelManager.DropItem(itemComponent, ((UnityEngine.Component)player).transform.position + Vector3.up);
						ShowFloatingText(nickname + "님이 파편을 소환했습니다!");
					}
					else
					{
						Characters.Gear.Weapons.Weapon weapon = prefab.GetComponent<Characters.Gear.Weapons.Weapon>();
						if ((UnityEngine.Object)(object)weapon != (UnityEngine.Object)null)
						{
							service.levelManager.DropWeapon(weapon, ((UnityEngine.Component)player).transform.position + Vector3.up);
							ShowFloatingText(nickname + "님이 파편 장비를 소환했습니다!");
						}
						else
						{
							Characters.Gear.Quintessences.Quintessence quint = prefab.GetComponent<Characters.Gear.Quintessences.Quintessence>();
							if ((UnityEngine.Object)(object)quint != (UnityEngine.Object)null)
							{
								service.levelManager.DropQuintessence(quint, ((UnityEngine.Component)player).transform.position + Vector3.up);
								ShowFloatingText(nickname + "님이 파편 정수를 소환했습니다!");
							}
							else
							{
								Characters.Gear.Fragments.Fragment fragment = prefab.GetComponent<Characters.Gear.Fragments.Fragment>();
								if ((UnityEngine.Object)(object)fragment != (UnityEngine.Object)null)
								{
									service.levelManager.DropFragment(fragment, ((UnityEngine.Component)player).transform.position + Vector3.up);
									ShowFloatingText(nickname + "님이 파편을 소환했습니다!");
								}
								else
								{
									ShowFloatingText("파편 소환 실패: 아이템/장비/정수/파편 아님! " + targetKey);
								}
							}
						}
					}
				}
				else
				{
					ShowFloatingText("파편 소환 실패: 프리팹 Null! " + targetKey);
				}
			}
			else
			{
				ShowFloatingText("파편 소환 실패: 에셋 로드 실패! " + targetKey);
			}
		}
		else
		{
			ShowFloatingText("파편 소환 실패!");
		}
	}

	private void DoFragmentDelete(string nickname)
	{
		Character player = GetPlayer();
		if (!((UnityEngine.Object)(object)player == (UnityEngine.Object)null))
		{
			Characters.Player.Inventory inventory = player.playerComponents.inventory;
			if (inventory != null && inventory.fragment != null && inventory.fragment.fragments.Count > 0)
			{
				int index = _random.Next(inventory.fragment.fragments.Count);
				inventory.fragment.Remove(index);
				ShowFloatingText(nickname + "님이 파편을 삭제했습니다!");
			}
			else
			{
				ShowFloatingText(nickname + "님의 파편 삭제 시도! (파편이 없네요)");
			}
		}
	}

	private IEnumerator DoSkullCoroutine(string nickname)
	{
		Service service = GetService();
		if ((UnityEngine.Object)(object)service == (UnityEngine.Object)null)
		{
			yield break;
		}
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			yield break;
		}
		int roll = _random.Next(1, 1001);
		WeaponReference weaponRef = null;
		
		if (roll <= 5)
		{
			// Load Skul_Super prefab directly from Addressables
			var op = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("Assets/Gear/Weapons/Skul_Super/Skul_Super.prefab");
			yield return op;
			if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
			{
				GameObject prefab = op.Result;
				if ((UnityEngine.Object)(object)prefab != (UnityEngine.Object)null)
				{
					Weapon superSkulPrefab = prefab.GetComponent<Weapon>();
					if ((UnityEngine.Object)(object)superSkulPrefab != (UnityEngine.Object)null)
					{
						service.levelManager.DropWeapon(superSkulPrefab, ((Component)player).transform.position + Vector3.up);
						ShowFloatingText(nickname + "님이 강화된 스컬을 소환했습니다!!");
						yield break;
					}
				}
			}
		}

		if (roll <= 300)
		{
			Weapon currentWeapon = player.playerComponents.inventory.weapon.polymorphOrCurrent;
			if ((UnityEngine.Object)(object)currentWeapon != (UnityEngine.Object)null && (int)((Gear)currentWeapon).rarity != 3 && currentWeapon.nextLevelReference != null && !string.IsNullOrEmpty(((GearReference)currentWeapon.nextLevelReference).name))
			{
				weaponRef = currentWeapon.nextLevelReference;
			}
		}
		if (weaponRef == null)
		{
			Rarity rarity = PickWeightedRarity();
			weaponRef = service.gearManager.GetWeaponToTake(rarity);
			if (weaponRef == null)
			{
				weaponRef = service.gearManager.GetWeaponToTake((Rarity)0);
			}
		}
		if (weaponRef == null)
		{
			yield break;
		}
		WeaponRequest request = weaponRef.LoadAsync();
		while (!((Request<Weapon>)(object)request).isDone)
		{
			yield return null;
		}
		int actionRoll = _random.Next(1, 101);
		if (actionRoll <= 50)
		{
			TryDeleteSubSkull();
			service.levelManager.DropWeapon(request, ((Component)player).transform.position + Vector3.up);
			ShowFloatingText(nickname + "이(가) 보조 스컬을 새 스컬로 강제 교체했어요!");
		}
		else if (actionRoll <= 70)
		{
			if (TryDeleteSubSkull())
			{
				ShowFloatingText(nickname + "이(가) 보조 스컬을 빼앗아갔어요!");
			}
			else
			{
				ShowFloatingText(nickname + "이(가) 보조 스컬을 뺏으려다 불발됐어요!");
			}
		}
		else
		{
			service.levelManager.DropWeapon(request, ((Component)player).transform.position + Vector3.up);
			ShowFloatingText(nickname + "이(가) 스컬을 선물했어요!");
		}
	}

	private void DoSynergy(string nickname)
	{
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Invalid comparison between Unknown and I4
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		Inventory inventory = player.playerComponents.inventory;
		if (inventory == null || (UnityEngine.Object)(object)inventory.synergy == (UnityEngine.Object)null || inventory.synergy.inscriptions == null)
		{
			return;
		}
		Synergy synergy = inventory.synergy;
		List<Inscription.Key> list = new List<Inscription.Key>();
		List<Inscription.Key> list2 = new List<Inscription.Key>();
		if (Inscription.keys != null)
		{
			foreach (Inscription.Key key in Inscription.keys)
			{
				if ((int)key != 0 && (int)key != 35)
				{
					if (synergy.inscriptions[key].count > 0)
					{
						list.Add(key);
					}
					else
					{
						list2.Add(key);
					}
				}
			}
		}
		int num = _random.Next(1, 101);
		string text = "";
		Inscription.Key val;
		if (num <= 50 && list.Count > 0)
		{
			val = list[_random.Next(list.Count)];
			text = "강화";
		}
		else
		{
			if (list2.Count <= 0)
			{
				ShowFloatingText(nickname + "의 각인 조작 시도! 하지만 실패했어요.");
				return;
			}
			val = list2[_random.Next(list2.Count)];
			text = "추가";
		}
		Inscription obj = synergy.inscriptions[val];
		obj.bonusCount++;
		if (text == "강화")
		{
			CustomSuperInscriptions.Add(val);
		}
		inventory.UpdateSynergy();
		ShowFloatingText($"{nickname}이(가) [{val}] 각인을 {text}했어요!");
	}

	private void DoOmen(string nickname)
	{
		OmenChestPath.ForceNextOmen = true;
		ShowFloatingText(nickname + "이(가) 다음 상자를 흉조 상자로 바꿨어요!");
	}

	private void DoDarkAbility(string nickname)
	{
		//IL_01af: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Invalid comparison between Unknown and I4
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null)
		{
			return;
		}
		UpgradeInventory upgrade = player.playerComponents.inventory.upgrade;
		if ((UnityEngine.Object)(object)upgrade == (UnityEngine.Object)null)
		{
			return;
		}
		int num = _random.Next(1, 101);
		if (num <= 50)
		{
			List<UpgradeObject> upgrades = upgrade.upgrades;
			List<int> list = new List<int>();
			for (int i = 0; i < upgrades.Count; i++)
			{
				if ((UnityEngine.Object)(object)upgrades[i] != (UnityEngine.Object)null)
				{
					list.Add(i);
				}
			}
			if (list.Count > 0)
			{
				int num2 = list[_random.Next(list.Count)];
				upgrade.Remove(num2);
				upgrade.Trim();
				ShowFloatingText(nickname + "이(가) 검은 능력을 삭제했어요!");
			}
			else
			{
				ShowFloatingText(nickname + "의 파괴 시도! 하지만 능력이 없어요.");
			}
			return;
		}
		UpgradeManager instance = Singleton<UpgradeManager>.Instance;
		if (!((UnityEngine.Object)(object)instance != (UnityEngine.Object)null))
		{
			return;
		}
		List<UpgradeResource.Reference> collection = instance.GetUnlockList((UpgradeObject.Type)0) ?? new List<UpgradeResource.Reference>();
		List<UpgradeResource.Reference> collection2 = instance.GetUnlockList((UpgradeObject.Type)1) ?? new List<UpgradeResource.Reference>();
		List<UpgradeResource.Reference> list2 = new List<UpgradeResource.Reference>();
		list2.AddRange(collection);
		list2.AddRange(collection2);
		if (list2.Count > 0)
		{
			UpgradeResource.Reference val = list2[_random.Next(list2.Count)];
			if (upgrade.TryEquip(val))
			{
				string text = (((int)val.type == 1) ? "저주" : "검은 능력");
				ShowFloatingText(nickname + "이(가) " + text + "을(를) 추가했어요!");
			}
			else
			{
				ShowFloatingText(nickname + "의 부여 시도! 칸이 가득 찼어요.");
			}
		}
		else
		{
			ShowFloatingText(nickname + "의 부여 시도! 획득 가능한 능력이 없어요.");
		}
	}

	private void DoBoss(string nickname)
	{
		StartCoroutine(C_DoBoss(nickname));
	}

	private IEnumerator C_DoBoss(string nickname)
	{
		// 디컴파일 기반: EnemyWaveContainer.Attach(character) + AIController.target 으로 올바르게 소환
		// SpawnRandomCharacter/SpawnCharacter 오퍼레이션 패턴을 따름
		Character player = GetPlayer();
		if ((UnityEngine.Object)(object)Map.Instance == (UnityEngine.Object)null ||
		    (UnityEngine.Object)(object)Map.Instance.waveContainer == (UnityEngine.Object)null)
		{
			ForceNextDarkEnemy = true;
			_darkEnemyTargetMap = null;
			ShowFloatingText(nickname + "이(가) 다음 맵을 저주했어요!");
			yield break;
		}
		List<Character> allEnemies = UnityEngine.Resources.FindObjectsOfTypeAll<Character>().ToList();
		
		HashSet<string> allowedBosses = new HashSet<string> {
			"ElderEnt(Hardmode)", "Dark", "First Hero Phase Dark (Sound)",
			"FirstHero (1 Phase)", "DarkSkeleton_Phase2", "DarkSkeleton_Phase1",
			"Emperor_Renewal", "Emperor4"
		};

		HashSet<string> allowedAdventurers = new HashSet<string> {
			"Veteran Hero", "Veteran Magician", "Veteran Cleric",
			"Veteran Thief", "Veteran Archer", "Veteran Warrior"
		};

		HashSet<string> allowedMobs = new HashSet<string> {
			"CarleonRecruitInCannon", "Hound", "LandWizard", "OldTreeEnt", "AssassinMercenary",
			"ForestRootKeeper", "CarleonRecruit", "CannonSpecialist", "EntApocalypse", "BD_Chaser",
			"ThunderCaller", "CarleonManAtArms", "BlossomEnt", "Cannoneer", "Occulist", "Ent",
			"CarleonAssassin", "GiganticEnt", "GiantMushroomEnt", "Dimensionalist (C1)",
			"Dimensionalist (C4)", "Butler", "GoldmaneSpearMan", "CarleonGoldRecruit", "WindBand",
			"GoldManeArcher", "GoldManeGuard", "Maid01", "ChiefMaid", "LordChamberlain",
			"GoldmaneLowClassWizard", "FireCaerleonLowClassWizard", "Servant", "GoldmaneRecruit",
			"GoldManeCavalry", "Silentist", "WindBandLeader", "StickySubject_S", "StickySubject_M_0",
			"StickySubject_M_1", "StickySubject_L", "StickySubject_S_0", "StickySubject_S_1",
			"Alchemist", "Heart", "Ultimate", "Transcendent", "ToxicAlchemist", "StrangeSubject",
			"LooseSubject", "PerfectSubject", "BD_HighAlchemist(Summoner)_1", "BD_HighAlchemist(Summoner)_2",
			"ArmoredGolem", "FanaticRecruit", "Bombardier", "HolyKnightsArcher", "LeoniaOfProtection",
			"Executioner", "DemonHunter", "Fanatic", "MartyrFanatic", "Moderator", "Awakened",
			"HolyKnightsMagician", "Wisdom", "Brave", "HereticInquisitor", "HolyKnightsPriest",
			"HolyKnightsRecruit", "Aged Fanatic", "HighFanatic", "Arbiter", "HolyKnightsManAtArms",
			"HolyKnightsSpearMan", "Fire", "Escort Orb", "Cross", "Cross (1)", "Cross (2)", "Sentinel"
		};
		
		int roll = _random.Next(100);
		Character.Type targetType = Character.Type.Boss;
		HashSet<string> targetAllowedNames = allowedBosses;

		Character original = null;

		List<string> addressableKeys = new List<string>();
		if (targetType == Character.Type.Boss || targetType == Character.Type.Adventurer)
		{
			foreach (var locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
			{
				foreach (object keyObj in locator.Keys)
				{
					string k = keyObj.ToString();
					if (k.EndsWith(".prefab") && k.Contains("Enemies") && !k.Contains("effect") && !k.Contains("ui"))
					{
						string fileName = System.IO.Path.GetFileNameWithoutExtension(k);
						if (targetAllowedNames.Contains(fileName))
						{
							addressableKeys.Add(k);
						}
					}
				}
			}
		}

		Character bossChar = null;
		Vector3 spawnPos = ((UnityEngine.Object)(object)player != (UnityEngine.Object)null) 
			? ((Component)player).transform.position + Vector3.right * ((_random.Next(2) == 0 ? 1f : -1f) * 3f) + Vector3.up * 0.5f 
			: Vector3.zero;

		if (addressableKeys.Count > 0)
		{
			string randomKey = addressableKeys[_random.Next(addressableKeys.Count)];
			var handle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(randomKey);
			yield return handle;
			
			if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
			{
				GameObject loadedObj = handle.Result;
				if (loadedObj != null)
				{
					bossChar = loadedObj.GetComponent<Character>();
					if (bossChar != null)
					{
						original = bossChar;
						loadedObj.transform.position = spawnPos;
					}
				}
			}
		}

		// 어드레서블 로딩에 실패했거나 키가 없다면 메모리 폴백
		if ((UnityEngine.Object)(object)bossChar == (UnityEngine.Object)null)
		{
			List<Character> candidates = allEnemies.Where(c =>
			{
				if ((UnityEngine.Object)(object)c == (UnityEngine.Object)null) return false;
				string n = c.gameObject.name.Replace("(Clone)", "").Trim();
				return targetAllowedNames.Contains(n);
			})
			.GroupBy(c => c.gameObject.name.Replace("(Clone)", "").Trim())
			.Select(g => g.FirstOrDefault(c => !c.gameObject.scene.IsValid()) ?? g.First())
			.ToList();

			// 해당 타입이 없으면 폴백으로 몹(TrashMob/Named)이라도 검색
			if (candidates.Count == 0 && targetType != Character.Type.TrashMob)
			{
				targetType = Character.Type.TrashMob;
				candidates = allEnemies.Where(c =>
				{
					if ((UnityEngine.Object)(object)c == (UnityEngine.Object)null) return false;
					string n = c.gameObject.name.Replace("(Clone)", "").Trim();
					return allowedMobs.Contains(n);
				})
				.GroupBy(c => c.gameObject.name.Replace("(Clone)", "").Trim())
				.Select(g => g.FirstOrDefault(c => !c.gameObject.scene.IsValid()) ?? g.First())
				.ToList();
			}

			if (candidates.Count > 0)
			{
				original = candidates[_random.Next(candidates.Count)];
			}
		}

		if ((UnityEngine.Object)(object)bossChar == (UnityEngine.Object)null && (UnityEngine.Object)(object)original == (UnityEngine.Object)null)
		{
			ForceNextDarkEnemy = true;
			_darkEnemyTargetMap = null;
			ShowFloatingText(nickname + "이(가) 소환할 몬스터가 없어 다음 맵을 저주했어요!");
			yield break;
		}

		try
		{
			if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null) yield break;

			// 메모리 원본으로 소환하는 경우 (어드레서블 직접 소환이 아닐 때)
			if ((UnityEngine.Object)(object)bossChar == (UnityEngine.Object)null)
			{
				bossChar = UnityEngine.Object.Instantiate<Character>(original, spawnPos, Quaternion.identity);
			}

			if ((UnityEngine.Object)(object)bossChar == (UnityEngine.Object)null) throw new Exception("Instantiate failed");

			Debug.Log($"[ChzzkGameMode] 스폰 완료! 이름: {bossChar.gameObject.name}, 타입: {bossChar.type}");

			((Component)bossChar).gameObject.SetActive(true);
			if (bossChar.attach != null)
			{
				bossChar.attach.SetActive(true);
			}
			
			// 스케일 증가 (잡몹, 네임드만 적용 - 미니보스 시각적 효과)
			if (bossChar.type == Character.Type.TrashMob || bossChar.type == Character.Type.Named)
			{
				((Component)bossChar).transform.localScale = ((Component)original).transform.localScale * 1.8f;
			}
			// 체력 풀 회복
			if (bossChar.health != null)
			{
				try
				{
					((Health)bossChar.health).Heal(999999.0, true);
					((Health)bossChar.health).SetCurrentHealth(((Health)bossChar.health).maximumHealth * 0.5);
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[ChzzkGameMode] Heal 실패: " + ex.Message);
				}
			}
			// AIController 통해 플레이어를 타겟으로 설정 (SpawnCharacter 오퍼레이션 패턴)
			Characters.AI.AIController aiCtrl = ((Component)bossChar).GetComponentInChildren<Characters.AI.AIController>();
			if ((UnityEngine.Object)(object)aiCtrl != (UnityEngine.Object)null)
			{
				aiCtrl.target = player;
				bossChar.ForceToLookAt(((Component)player).transform.position.x);
				try { aiCtrl.FoundEnemy(); } catch { }
			}
			// EnemyWaveContainer.Attach 로 wave에 등록 (waveContainer.summonWave에 추가됨)
			if ((UnityEngine.Object)(object)Map.Instance != (UnityEngine.Object)null && (UnityEngine.Object)(object)Map.Instance.waveContainer != (UnityEngine.Object)null)
			{
				try
				{
					Map.Instance.waveContainer.Attach(bossChar);
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[ChzzkGameMode] waveContainer.Attach 실패: " + ex.Message);
				}
			}
			// 색상 변경 (잡몹, 네임드만 적용. 보스/모험가는 원래 색상 유지)
			if (bossChar.type == Character.Type.TrashMob || bossChar.type == Character.Type.Named)
			{
				SpriteRenderer bossRenderer = ((Component)bossChar).GetComponentInChildren<SpriteRenderer>();
				if ((UnityEngine.Object)(object)bossRenderer != (UnityEngine.Object)null)
				{
					bossRenderer.color = new Color(0.8f, 0.1f, 0.1f);
				}
			}

			
			ForceNextDarkEnemy = true;
			_darkEnemyTargetMap = null;
			ShowFloatingText(nickname + "이(가) 보스를 소환하고 다음 맵을 저주했어요!");
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[ChzzkGameMode] DoBoss 소환 실패: " + ex));
			// 소환 실패 시 기존 적을 강화하는 방식으로 fallback
			List<Character> fallbackCandidates = Map.Instance.waveContainer.GetAllSpawnedEnemies();
			if (fallbackCandidates.Count > 0)
			{
				Character fallbackChar = fallbackCandidates[_random.Next(fallbackCandidates.Count)];
				((Component)fallbackChar).transform.localScale *= 1.5f;
				try
				{
					fallbackChar.stat.AttachValues(new Values((Value[])(object)new Value[1]
					{
						new Value(Category.PercentPoint, Kind.Health, 5.0)
					}));
					fallbackChar.stat.AttachValues(new Values((Value[])(object)new Value[1]
					{
						new Value(Category.PercentPoint, Kind.AttackDamage, 1.5)
					}));
				}
				catch { }
				((Health)fallbackChar.health).Heal(999999.0, true);
			}
			ForceNextDarkEnemy = true;
			_darkEnemyTargetMap = null;
			ShowFloatingText(nickname + "이(가) 기존 적을 강화하고 다음 맵을 저주했어요!");
		}
	}

	private Character GetPlayer()
	{
		try
		{
			Service instance = Singleton<Service>.Instance;
			object result;
			if (instance == null)
			{
				result = null;
			}
			else
			{
				LevelManager levelManager = instance.levelManager;
				result = ((levelManager != null) ? levelManager.player : null);
			}
			return (Character)result;
		}
		catch
		{
			return null;
		}
	}

	private Service GetService()
	{
		try
		{
			return Singleton<Service>.Instance;
		}
		catch
		{
			return null;
		}
	}

	private Rarity PickWeightedRarity()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		int num = _random.Next(100);
		if (num < 60)
		{
			return (Rarity)0;
		}
		if (num < 90)
		{
			return (Rarity)1;
		}
		if (num < 99)
		{
			return (Rarity)2;
		}
		return (Rarity)3;
	}

	private void ShowFloatingText(string message)
	{
		if (message.Contains("$CHAOS$"))
		{
			message = "무언가가 일어났습니다!";
		}
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			Service instance = Singleton<Service>.Instance;
			object obj;
			if (instance == null)
			{
				obj = null;
			}
			else
			{
				LevelManager levelManager = instance.levelManager;
				obj = ((levelManager != null) ? levelManager.player : null);
			}
			Character val = (Character)obj;
			if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
			{
				return;
			}
			Service instance2 = Singleton<Service>.Instance;
			if (instance2 != null)
			{
				FloatingTextSpawner floatingTextSpawner = instance2.floatingTextSpawner;
				if (floatingTextSpawner != null)
				{
					floatingTextSpawner.SpawnBuff(message, ((Component)val).transform.position + Vector3.up * 2f, "#FFD700");
				}
			}
		}
		catch
		{
		}
	}

	// ApplyCustomDarkEnemyRoutine Removed (replaced by DarkEnemySelector patch)

	private void StartVote()
	{
		if (_voteState != VoteState.Inactive)
		{
			return;
		}
		_votedUsers.Clear();
		_currentVoteOptions.Clear();
		_voteTimer = Plugin.VoteDurationSeconds.Value;
		List<VoteOption> list = new List<VoteOption>
		{
			new VoteOption
			{
				Title = "랜덤 스컬 소환",
				Votes = 0,
				Action = delegate(string n)
				{
					((MonoBehaviour)this).StartCoroutine(DoSkullCoroutine(n));
				}
			},
			new VoteOption
			{
				Title = "랜덤 아이템 소환",
				Votes = 0,
				Action = delegate(string n)
				{
					((MonoBehaviour)this).StartCoroutine(DoItemCoroutine(n));
				}
			},
			new VoteOption
			{
				Title = "랜덤 스탯 변화",
				Votes = 0,
				Action = delegate(string n)
				{
					DoRandomStat(n);
				}
			},
			new VoteOption
			{
				Title = "버프 또는 은총(저주)",
				Votes = 0,
				Action = delegate(string n)
				{
					DoBuffOrCurse(n);
				}
			},
			new VoteOption
			{
				Title = "각인 추가",
				Votes = 0,
				Action = delegate(string n)
				{
					DoSynergy(n);
				}
			},
			new VoteOption
			{
				Title = "보스 소환",
				Votes = 0,
				Action = delegate(string n)
				{
					DoBoss(n);
				}
			},
			new VoteOption
			{
				Title = "음식(회복) 드롭",
				Votes = 0,
				Action = delegate(string n)
				{
					DoFood(n);
				}
			},
			new VoteOption
			{
				Title = "랜덤 NPC 스폰",
				Votes = 0,
				Action = delegate(string n)
				{
					DoNPC(n);
				}
			},
			new VoteOption
			{
				Title = "파편 (랜덤 소환/삭제)",
				Votes = 0,
				Action = delegate(string n) { ((UnityEngine.MonoBehaviour)this).StartCoroutine(DoFragmentCoroutine(n)); }
			},
			new VoteOption
			{
				Title = "뼈 조각 소환",
				Votes = 0,
				Action = delegate(string n) { DoBone(n); }
			},
			new VoteOption
			{
				Title = "골드 지급",
				Votes = 0,
				Action = delegate(string n) { DoGold(n); }
			},
			new VoteOption
			{
				Title = "검은 마석 지급",
				Votes = 0,
				Action = delegate(string n) { DoDarkQuartz(n); }
			},
			new VoteOption
			{
				Title = "정수 소환",
				Votes = 0,
				Action = delegate(string n) { ((UnityEngine.MonoBehaviour)this).StartCoroutine(DoQuintessenceCoroutine(n)); }
			},
			new VoteOption
			{
				Title = "검은 능력 지급",
				Votes = 0,
				Action = delegate(string n) { DoDarkAbility(n); }
			},
			new VoteOption
			{
				Title = "흉조 아이템 드롭",
				Votes = 0,
				Action = delegate(string n) { DoOmen(n); }
			},
			new VoteOption
			{
				Title = "체력 회복",
				Votes = 0,
				Action = delegate(string n) { DoHeal(n); }
			},
			new VoteOption
			{
				Title = "방어력 10% 증가",
				Votes = 0,
				Action = delegate(string n) { DoDefense(n); }
			}
		};
		for (int num = 0; num < 3; num++)
		{
			if (list.Count <= 0)
			{
				break;
			}
			int index = _random.Next(list.Count);
			_currentVoteOptions.Add(list[index]);
			list.RemoveAt(index);
		}
		_voteState = VoteState.Voting;
		ShowFloatingText("투표가 시작되었습니다! 채팅창에 번호를 입력하세요!");
	}

	private void EndVote()
	{
		_voteState = VoteState.Processing;
		if (_currentVoteOptions.Count > 0)
		{
			VoteOption voteOption = _currentVoteOptions.OrderByDescending((VoteOption o) => o.Votes).First();
			if (voteOption.Votes == 0)
			{
				voteOption = _currentVoteOptions[_random.Next(_currentVoteOptions.Count)];
				ShowFloatingText("아무도 투표하지 않아 랜덤으로 [" + voteOption.Title + "] 이(가) 선택되었습니다!");
			}
			else
			{
				ShowFloatingText($"투표 종료! [{voteOption.Title}] 이(가) {voteOption.Votes}표로 선택되었습니다!");
			}
			try
			{
				voteOption.Action?.Invoke("시청자 투표");
			}
			catch (Exception ex)
			{
				Debug.LogError((object)("[ChzzkGameMode] 투표 액션 실행 오류: " + ex));
			}
		}
		_voteState = VoteState.Inactive;
	}
}
