using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Characters;
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

	private Random _random = new Random();

	public static bool ForceNextDarkEnemy = false;

	private Map _lastMap;

	private static ChzzkGameMode _instance;

	public static HashSet<Key> CustomSuperInscriptions = new HashSet<Key>();

	private Map _currentMap;

	private bool _streamerUsedCommandInThisMap = false;

	private VoteState _voteState = VoteState.Inactive;

	private float _voteTimer;

	private float _voteAutoTimer;

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

	private HashSet<string> FindNpcCommands => GetCommands(Plugin.CmdFindNpcString.Value);

	private HashSet<string> BoneCommands => GetCommands(Plugin.CmdBoneString.Value);

	private HashSet<string> GoldCommands => GetCommands(Plugin.CmdGoldString.Value);

	private HashSet<string> DarkQuartzCommands => GetCommands(Plugin.CmdDarkQuartzString.Value);

	private HashSet<string> QuintessenceCommands => GetCommands(Plugin.CmdQuintessenceString.Value);

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
		if ((Object)(object)_instance != (Object)null && (Object)(object)_instance != (Object)(object)this)
		{
			Object.Destroy((Object)(object)((Component)this).gameObject);
			return;
		}
		_instance = this;
		Object.DontDestroyOnLoad((Object)(object)((Component)this).gameObject);
	}

	private void Update()
	{
		Map instance = Map.Instance;
		if ((Object)(object)instance != (Object)null && (Object)(object)instance != (Object)(object)_currentMap)
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
		if ((Object)(object)Map.Instance != (Object)null && (Object)(object)_lastMap != (Object)(object)Map.Instance)
		{
			_lastMap = Map.Instance;
			if (Plugin.EnableVote.Value && Plugin.VoteTriggerMode.Value.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0 && _voteState == VoteState.Inactive)
			{
				StartVote();
			}
			if (ForceNextDarkEnemy)
			{
				ForceNextDarkEnemy = false;
				Debug.Log((object)"[ChzzkGameMode] 다음 맵 커스텀 몬스터 강화 적용 완료");
				ShowFloatingText("강화된 몬스터들이 등장합니다!");
				((MonoBehaviour)this).StartCoroutine(ApplyCustomDarkEnemyRoutine());
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
				ShowFloatingText($"명령어 쿨타임.. ({_cooldownTimer:F1}초 남음)");
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
					((MonoBehaviour)this).StartCoroutine(DoQuintessenceCoroutine(nickname));
				}
				else if (FindNpcCommands.Contains(command))
				{
					DoFindNPC(nickname);
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
		if ((Object)(object)GUI.skin != (Object)null && GUI.skin.label != null)
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
		Rect rect = new Rect((float)(Screen.width - 320), 20f, 300f, 50f);
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

	public void OnChatMessage(string nickname, string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}
		string text = message.Trim().ToLower();
		nickname = (string.IsNullOrEmpty(nickname) ? "익명" : nickname);
		if (_voteState == VoteState.Voting && int.TryParse(text.Replace("!", ""), out var result) && result >= 1 && result <= _currentVoteOptions.Count && !_votedUsers.Contains(nickname))
		{
			_votedUsers.Add(nickname);
			_currentVoteOptions[result - 1].Votes++;
		}
		else if (nickname == Plugin.StreamerNickname.Value)
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
		else if (HealCommands.Contains(text) || BuffCommands.Contains(text) || ItemCommands.Contains(text) || SkullCommands.Contains(text) || SynergyCommands.Contains(text) || OmenCommands.Contains(text) || BossCommands.Contains(text) || DarkCommands.Contains(text) || RandomCommands.Contains(text) || RandomStatCommands.Contains(text) || NpcCommands.Contains(text) || FoodCommands.Contains(text) || BoneCommands.Contains(text) || GoldCommands.Contains(text) || DarkQuartzCommands.Contains(text) || QuintessenceCommands.Contains(text) || FindNpcCommands.Contains(text))
		{
			_commandQueue.Enqueue(new CommandInfo
			{
				nickname = nickname,
				command = text,
				isStreamer = false
			});
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
			DoDonationDarkAbility(nickname);
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
		DoRandomStat(nickname);
	}

	private void DoBone(string nickname)
	{
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
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
				levelManager.DropCurrency((Type)2, num, num, ((Component)player).transform.position + Vector3.up * 1.5f);
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
		if ((Object)(object)player == (Object)null)
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
				levelManager.DropCurrency((Type)0, num, 10, ((Component)player).transform.position + Vector3.up * 1.5f);
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
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		int num = _random.Next(50, 150);
		Service service = GetService();
		if (service != null)
		{
			LevelManager levelManager = service.levelManager;
			if (levelManager != null)
			{
				levelManager.DropCurrency((Type)1, num, 10, ((Component)player).transform.position + Vector3.up * 1.5f);
			}
		}
		ShowFloatingText($"{nickname}님이 마석 {num}개를 떨어뜨렸습니다!");
	}

	private void DoFood(string nickname)
	{
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		string[] array = new string[3] { "Apple", "Meat", "Potion" };
		GameObject val = null;
		string[] array2 = array;
		foreach (string foodName in array2)
		{
			val = Object.FindObjectsOfType<GameObject>(true).FirstOrDefault((GameObject g) => ((Object)g).name.IndexOf(foodName, StringComparison.OrdinalIgnoreCase) >= 0 && (Object)(object)g.GetComponent<Collider2D>() != (Object)null);
			if ((Object)(object)val != (Object)null)
			{
				break;
			}
		}
		if ((Object)(object)val != (Object)null)
		{
			Object.Instantiate<GameObject>(val, ((Component)player).transform.position + Vector3.up * 2f, Quaternion.identity);
			ShowFloatingText(nickname + "님이 맛있는 음식을 떨어뜨렸어요!");
		}
		else
		{
			double num = ((Health)player.health).PercentHeal(0.2f);
			ShowFloatingText(nickname + "님이 음식을 선물했어요! (체력 회복)");
		}
	}

	private void DoNPC(string nickname)
	{
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		string[] array = new string[6] { "Field_Fox", "Field_Ogre", "MagicalSlime", "DarkPriest", "HalflingGirl", "Field_DeathKnight" };
		string chosenNpcName = array[_random.Next(array.Length)];
		GameObject[] source = Object.FindObjectsOfType<GameObject>(true);
		GameObject val = source.FirstOrDefault((GameObject g) => ((Object)g).name.Contains(chosenNpcName));
		if ((Object)(object)val == (Object)null)
		{
			val = source.FirstOrDefault((GameObject g) => ((Object)g).name.Contains("Field_") || ((Object)g).name.Contains("Npc"));
		}
		if ((Object)(object)val != (Object)null)
		{
			Object.Instantiate<GameObject>(val, ((Component)player).transform.position + Vector3.right * 1.5f, Quaternion.identity);
			ShowFloatingText(nickname + "님이 NPC를 소환했습니다! (" + ((Object)val).name + ")");
		}
		else
		{
			ShowFloatingText(nickname + "님이 부른 NPC가 오지 못해서 대신 버프를 남겼습니다!");
			DoBuffOrCurse("유령 NPC");
		}
	}

	private void DoFindNPC(string nickname)
	{
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			MonoBehaviour[] array = Object.FindObjectsOfType<MonoBehaviour>();
			HashSet<string> hashSet = new HashSet<string>();
			MonoBehaviour[] array2 = array;
			foreach (MonoBehaviour val in array2)
			{
				string name = ((object)val).GetType().Name;
				if (name.IndexOf("NPC", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Interact", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					GameObject gameObject = ((Component)val).gameObject;
					string text = ((Object)gameObject).name;
					Transform parent = gameObject.transform.parent;
					while ((Object)(object)parent != (Object)null)
					{
						text = ((Object)parent).name + "/" + text;
						parent = parent.parent;
					}
					string text2 = $"[FindNPC] Type: {name}, Path: {text}, Pos: {gameObject.transform.position}";
					if (hashSet.Add(text2))
					{
						Debug.Log((object)text2);
					}
				}
			}
			ShowFloatingText(nickname + "님, 맵의 NPC/상자 정보를 로그창에 기록했습니다!");
			Debug.Log((object)$"[FindNPC] 총 {hashSet.Count}개의 관련 오브젝트를 찾았습니다.");
		}
		catch (Exception arg)
		{
			Debug.LogError((object)$"[FindNPC] 에러: {arg}");
			ShowFloatingText("NPC 찾기 중 에러가 발생했습니다.");
		}
	}

	private IEnumerator DoDonationItemCoroutine(string nickname)
	{
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
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
		if ((Object)(object)service == (Object)null)
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
			if (player.playerComponents.inventory.item.items.Count((Item i) => (Object)(object)i != (Object)null) < 9)
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
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		Inventory inventory = player.playerComponents.inventory;
		if (inventory == null || (Object)(object)inventory.synergy == (Object)null || inventory.synergy.inscriptions == null)
		{
			return;
		}
		Synergy synergy = inventory.synergy;
		List<Key> list = new List<Key>();
		List<Key> list2 = new List<Key>();
		if (Inscription.keys != null)
		{
			foreach (Key key in Inscription.keys)
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
				Key val = list[_random.Next(list.Count)];
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
		Key val2;
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
		if (!((Object)(object)player == (Object)null))
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
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		UpgradeInventory upgrade = player.playerComponents.inventory.upgrade;
		if ((Object)(object)upgrade == (Object)null)
		{
			return;
		}
		List<UpgradeObject> upgrades = upgrade.upgrades;
		List<int> list = new List<int>();
		for (int i = 0; i < upgrades.Count; i++)
		{
			if ((Object)(object)upgrades[i] != (Object)null)
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
		if ((Object)(object)player == (Object)null)
		{
			return false;
		}
		WeaponInventory weapon = player.playerComponents.inventory.weapon;
		if ((Object)(object)weapon == (Object)null)
		{
			return false;
		}
		try
		{
			Type type = ((object)weapon).GetType();
			PropertyInfo propertyInfo = type.GetProperty("weapons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetProperty("nextWeapons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo fieldInfo = type.GetField("weapons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("_weapons", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			IEnumerable enumerable = null;
			if (propertyInfo != null)
			{
				enumerable = propertyInfo.GetValue(weapon) as IEnumerable;
			}
			else if (fieldInfo != null)
			{
				enumerable = fieldInfo.GetValue(weapon) as IEnumerable;
			}
			if (enumerable != null)
			{
				Weapon polymorphOrCurrent = weapon.polymorphOrCurrent;
				foreach (object item in enumerable)
				{
					Weapon val = (Weapon)((item is Weapon) ? item : null);
					if ((Object)(object)val != (Object)null && (Object)(object)val != (Object)(object)polymorphOrCurrent)
					{
						MethodInfo methodInfo = type.GetMethod("Remove", new Type[1] { typeof(Weapon) }) ?? type.GetMethod("ForceDrop", new Type[1] { typeof(Weapon) }) ?? type.GetMethod("Drop", new Type[1] { typeof(Weapon) }) ?? type.GetMethod("Discard", new Type[1] { typeof(Weapon) }) ?? type.GetMethod("Lose", new Type[1] { typeof(Weapon) });
						if (methodInfo != null)
						{
							methodInfo.Invoke(weapon, new object[1] { val });
							Debug.Log((object)("[ChzzkGameMode] Successfully dropped sub-skull via " + methodInfo.Name));
							return true;
						}
						MethodInfo method = ((object)val).GetType().GetMethod("Drop", BindingFlags.Instance | BindingFlags.Public);
						if (method != null)
						{
							method.Invoke(val, null);
							Debug.Log((object)"[ChzzkGameMode] Successfully dropped sub-skull via Weapon.Drop()");
							return true;
						}
						Object.Destroy((Object)(object)((Component)val).gameObject);
						Debug.Log((object)"[ChzzkGameMode] Destroyed sub-skull object directly.");
						return true;
					}
				}
				Debug.LogWarning((object)"[ChzzkGameMode] Sub-skull not found in weapons list (or only 1 skull equipped).");
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
		if ((Object)(object)player == (Object)null)
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
		if ((Object)(object)player == (Object)null)
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
		if ((Object)(object)upgrade != (Object)null)
		{
			List<UpgradeObject> upgrades = upgrade.upgrades;
			for (int num2 = upgrades.Count - 1; num2 >= 0; num2--)
			{
				if ((Object)(object)upgrades[num2] != (Object)null)
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

	private void DoRandomStat(string nickname)
	{
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected O, but got Unknown
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Expected O, but got Unknown
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		Kind[] array = (Kind[])(object)new Kind[7]
		{
			Kind.AttackDamage,
			Kind.PhysicalAttackDamage,
			Kind.MagicAttackDamage,
			Kind.AttackSpeed,
			Kind.MovementSpeed,
			Kind.CriticalChance,
			Kind.CriticalDamage
		};
		string[] array2 = new string[7] { "모든 공격력", "물리 공격력", "마법 공격력", "공격 속도", "이동 속도", "치명타 확률", "치명타 데미지" };
		int num = _random.Next(array.Length);
		Kind val = array[num];
		string text = array2[num];
		bool flag = _random.Next(2) == 0;
		float num2 = (flag ? 0.1f : (-0.1f));
		string text2 = (flag ? "증가" : "감소");
		string text3 = (flag ? "축복" : "저주");
		try
		{
			player.stat.AttachValues(new Values((Value[])(object)new Value[1]
			{
				new Value(Category.PercentPoint, val, (double)num2)
			}));
			ShowFloatingText(nickname + "이(가) " + text3 + "을 내렸어요! " + text + " 10% " + text2 + "!");
		}
		catch
		{
			ShowFloatingText(nickname + "의 스탯 조작이 빗나갔어요!");
		}
	}

	private void DoHeal(string nickname)
	{
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
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
		if ((Object)(object)player == (Object)null)
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

	private unsafe void DoBuffOrCurse(string nickname)
	{
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Expected I4, but got Unknown
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		SavableAbilityManager component = ((Component)player).GetComponent<SavableAbilityManager>();
		if (!((Object)(object)component == (Object)null))
		{
			int num = _random.Next(1, 101);
			if (num <= 60)
			{
				Name[] array = new Name[3];
				RuntimeHelpers.InitializeArray(array, (RuntimeFieldHandle)/*OpCode not supported: LdMemberToken*/);
				Name[] array2 = (Name[])(object)array;
				Name val = array2[_random.Next(array2.Length)];
				component.Apply(val, 1f, 30f);
				ShowFloatingText(nickname + "이(가) " + (val - 8) switch
				{
					0 => "잔혹함(공격력↑)", 
					1 => "분노(공격속도↑)", 
					2 => "불굴(방어력↑)", 
					_ => ((object)(*(Name*)(&val))/*cast due to constrained. prefix*/).ToString(), 
				} + " 버프를 선물했어요!");
			}
			else
			{
				component.Apply((Name)0, 1f);
				ShowFloatingText(nickname + "이(가) 신의 은총을 선물해주셨습니다!");
			}
		}
	}

	private IEnumerator DoItemCoroutine(string nickname)
	{
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
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
		if ((Object)(object)service == (Object)null)
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
			if (randDrop <= 70 && player.playerComponents.inventory.item.items.Count((Item val) => (Object)(object)val != (Object)null) < 9)
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

	private IEnumerator DoSkullCoroutine(string nickname)
	{
		Service service = GetService();
		if ((Object)(object)service == (Object)null)
		{
			yield break;
		}
		Character player = GetPlayer();
		if ((Object)(object)player == (Object)null)
		{
			yield break;
		}
		int roll = _random.Next(1, 1001);
		WeaponReference weaponRef = null;
		if (roll <= 5)
		{
			FieldInfo weaponsField = ((object)service.gearManager).GetType().GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic) ?? ((object)service.gearManager).GetType().GetField("weapons", BindingFlags.Instance | BindingFlags.Public);
			if (weaponsField != null && weaponsField.GetValue(service.gearManager) is IEnumerable<WeaponReference> allWeapons)
			{
				foreach (WeaponReference w in allWeapons)
				{
					if (((GearReference)w).name.Equals("super_skul", StringComparison.OrdinalIgnoreCase))
					{
						weaponRef = w;
						break;
					}
				}
			}
		}
		else if (roll <= 305)
		{
			Weapon currentWeapon = player.playerComponents.inventory.weapon.polymorphOrCurrent;
			if ((Object)(object)currentWeapon != (Object)null && (int)((Gear)currentWeapon).rarity != 3 && currentWeapon.nextLevelReference != null && !string.IsNullOrEmpty(((GearReference)currentWeapon.nextLevelReference).name))
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
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		Inventory inventory = player.playerComponents.inventory;
		if (inventory == null || (Object)(object)inventory.synergy == (Object)null || inventory.synergy.inscriptions == null)
		{
			return;
		}
		Synergy synergy = inventory.synergy;
		List<Key> list = new List<Key>();
		List<Key> list2 = new List<Key>();
		if (Inscription.keys != null)
		{
			foreach (Key key in Inscription.keys)
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
		Key val;
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
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		UpgradeInventory upgrade = player.playerComponents.inventory.upgrade;
		if ((Object)(object)upgrade == (Object)null)
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
				if ((Object)(object)upgrades[i] != (Object)null)
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
		if (!((Object)(object)instance != (Object)null))
		{
			return;
		}
		List<Reference> collection = instance.GetUnlockList((Type)0) ?? new List<Reference>();
		List<Reference> collection2 = instance.GetUnlockList((Type)1) ?? new List<Reference>();
		List<Reference> list2 = new List<Reference>();
		list2.AddRange(collection);
		list2.AddRange(collection2);
		if (list2.Count > 0)
		{
			Reference val = list2[_random.Next(list2.Count)];
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
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Expected O, but got Unknown
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Expected O, but got Unknown
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Expected O, but got Unknown
		bool flag = false;
		if ((Object)(object)Map.Instance != (Object)null && (Object)(object)Map.Instance.waveContainer != (Object)null)
		{
			List<Character> list = Map.Instance.waveContainer.GetAllEnemies().ToList();
			if (list.Count > 0)
			{
				Character val = list[_random.Next(list.Count)];
				Transform transform = ((Component)val).transform;
				transform.localScale *= 1.5f;
				try
				{
					val.stat.AttachValues(new Values((Value[])(object)new Value[1]
					{
						new Value(Category.PercentPoint, Kind.Health, 5.0)
					}));
					val.stat.AttachValues(new Values((Value[])(object)new Value[1]
					{
						new Value(Category.PercentPoint, Kind.AttackDamage, 1.5)
					}));
				}
				catch
				{
				}
				((Health)val.health).Heal(999999.0, true);
				flag = true;
			}
		}
		ForceNextDarkEnemy = true;
		if (flag)
		{
			ShowFloatingText(nickname + "이(가) 몬스터 하나를 미니보스로 둔갑시키고 다음 맵을 저주했어요!");
		}
		else
		{
			ShowFloatingText(nickname + "이(가) 보스를 소환했어요! 다음 맵에서 적들이 강해집니다!");
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
			if ((Object)(object)val == (Object)null)
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

	private IEnumerator ApplyCustomDarkEnemyRoutine()
	{
		Map map = Map.Instance;
		HashSet<Character> processed = new HashSet<Character>();
		while ((Object)(object)Map.Instance == (Object)(object)map)
		{
			if ((Object)(object)map.waveContainer != (Object)null)
			{
				List<Character> enemies = map.waveContainer.GetAllEnemies();
				if (enemies != null)
				{
					foreach (Character c in enemies)
					{
						if (!((Object)(object)c != (Object)null) || processed.Contains(c))
						{
							continue;
						}
						processed.Add(c);
						string keyStr = ((object)c.key/*cast due to constrained. prefix*/).ToString();
						if ((int)c.type == 0 && keyStr != "Hound" && keyStr != "SpiritInFlask" && keyStr != "UnstableFlask" && keyStr != "UnstableFlasksSpirit" && keyStr != "Ent" && keyStr != "CannonSpecialist" && keyStr != "GiantMushroomEnt" && keyStr != "CarleonRecruitInCannon" && keyStr != "CarleonRecruit" && keyStr != "Unspecified")
						{
							Transform transform = ((Component)c).transform;
							transform.localScale *= 1.3f;
							try
							{
								c.stat.AttachValues(new Values((Value[])(object)new Value[1]
								{
									new Value(Category.PercentPoint, Kind.Health, 2.0)
								}));
								c.stat.AttachValues(new Values((Value[])(object)new Value[1]
								{
									new Value(Category.PercentPoint, Kind.AttackDamage, 1.2000000476837158)
								}));
							}
							catch
							{
							}
							SpriteRenderer renderer = ((Component)c).GetComponentInChildren<SpriteRenderer>();
							if ((Object)(object)renderer != (Object)null)
							{
								renderer.color = new Color(0.3f, 0f, 0.3f);
							}
						}
					}
				}
			}
			yield return (object)new WaitForSeconds(0.5f);
		}
	}

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
