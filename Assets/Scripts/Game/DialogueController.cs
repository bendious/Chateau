using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;


[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public class DialogueController : MonoBehaviour
{
	[SerializeField] private RectTransform m_canvasBackdropTf;
	[SerializeField] private UnityEngine.UI.Image m_image;
	[SerializeField] private TMP_Text m_text;
	[SerializeField] private GameObject m_continueIndicator;
	public Transform m_replyMenu;
	[SerializeField] private GameObject m_replyTemplate;

	[SerializeField] private float m_worldspaceWidth = 8.0f;
	[SerializeField] private float m_worldspaceSpacing = 0.5f;
	[SerializeField] private string m_worldspaceLayerName = "UI"; // TODO: Editor drop-down list?

	[SerializeField] private float m_revealSeconds = 0.05f;
	[SerializeField] private float m_revealSecondsFast = 0.005f;
	[SerializeField] private float m_newlineSecondsMin = 0.5f; // TODO: split into avatar/AI variants?

	[SerializeField] private float m_indicatorSpacing = 7.5f;

	[SerializeField] private string[] m_merchantFollowUpCriticalPath = new[] { "{merchantSellCriticalPath.}" };
	[SerializeField] private string[] m_merchantFollowUpSell = new[] { "{merchantSellPost.}" };
	[SerializeField] private string[] m_merchantFollowUpCancel = new[] { "{merchantSellCanceled.}" };


	[Serializable] public class Line
	{
		[Serializable] public class Reply
		{
			public string m_text;
			public string m_preconditionName;
			public int m_userdata;
			public float m_relationshipMin = 0.0f;
			public float m_relationshipMax = 1.0f;
			public float m_relationshipIncrement = 0.0f; // TODO: non-symmetric relationships?
			public string[] m_followUp;
			public string m_eventName; // TODO: less error-prone type?
			public bool m_breakAfterward;

			internal bool m_deactivated = false; // NOTE that this is due to SendMessage() not being able to return values and also so that OnReplySelected() doesn't re-run condition checks
		}

		public string m_text;
		[SerializeField] internal Dialogue m_source;
		[SerializeField] internal float m_sourceDistMax = float.MaxValue;
		public Reply[] m_replies;
	}


	public bool IsPlaying => gameObject.activeSelf;

	public bool CanInterrupt(KinematicCharacter newInteractor)
	{
		// allow avatar to cancel non-avatar offscreen dialogue
		if (m_sourceMain == null || newInteractor is not AvatarController || Target is AvatarController)
		{
			return false;
		}
		// TODO: more robust offscreen checks?
		Vector3 sourceViewPos = Camera.main.WorldToViewportPoint(m_sourceMain.transform.position);
		return sourceViewPos.x < 0.0f || sourceViewPos.x > 1.0f || sourceViewPos.y < 0.0f || sourceViewPos.y > 1.0f;
	}


	private Transform ReplyParentTf => m_replyTemplate.transform.parent;


	private static readonly Regex m_tagMatcher = new(@"<(.+)>.*?</\1>"); // this matches corresponding start/end tags along w/ the contents between them // NOTE the lazy rather than greedy wildcard matching to prevent multiple sets of identical tags being combined into one group // TODO: handle identical nested tags (via balancing group expressions? - https://learn.microsoft.com/en-us/dotnet/standard/base-types/grouping-constructs-in-regular-expressions#balancing-group-definitions)?
	private static readonly Regex m_commaSpaceRemovalMatcher = new(@"((?<=[^\w'"">]),\s+|,?\s+(?=[^\w'""<]))"); // this matches whitespace/comma-whitespace that is not preceded and followed by word characters or quotation marks or HTML tag brackets // NOTE the lookahead/lookbehind assertions to match non-word characters and string start/end w/o including them in the comma-whitespace match value; https://learn.microsoft.com/en-us/dotnet/standard/base-types/grouping-constructs-in-regular-expressions#zero-width-positive-lookahead-assertions

	private AudioSource m_audio;
	private RectTransform m_textTf;
	private Canvas m_canvas;
	private RectTransform m_canvasTf;

	private int m_canvasLayerOrig;
	private float m_canvasOffsetOrig;

	private Queue<Line> m_queue;
	private int m_revealedCharCount;
	private float m_lastRevealTime;
	private bool m_submitReleasedSinceNewline;
	private int m_replyIdx;
	private Queue<string> m_queueFollowUp;
	private GameObject m_callbackObject;
	public KinematicCharacter Target { get; private set; }
	private Component m_sourceMain;
	private int m_loopIdx = -1;

	private bool m_forceNewLine = false;
	public bool Canceled { get; private set; }
	public void Cancel() => Canceled = true;

	private float m_dialogueTimePrevious = float.MinValue; // NOTE that this is set at the END, not the beginning, of each dialogue
	public float TimeSincePreviousDialogue => Time.time - m_dialogueTimePrevious;

	private int m_merchantType = -1;
	private int m_merchantTargetIdx = -1;


	private void Awake()
	{
		m_audio = GetComponent<AudioSource>();
		m_textTf = m_text.GetComponent<RectTransform>();
		m_canvas = GetComponent<Canvas>();
		m_canvasTf = m_canvas.GetComponent<RectTransform>();
		m_canvasLayerOrig = m_canvas.sortingLayerID;
		m_canvasOffsetOrig = m_canvasBackdropTf.offsetMin.x; // TODO: don't assume min/max offset equality?
	}

	private void OnEnable() => OnHealthDecrement.OnExecute += OnDamage;

	private void OnDisable() => OnHealthDecrement.OnExecute -= OnDamage;

	private void OnDamage(OnHealthDecrement evt)
	{
		if (!IsPlaying || Target is not AvatarController || (evt.m_health.gameObject != Target.gameObject && (m_sourceMain == null || evt.m_health.gameObject != m_sourceMain.gameObject))) // NOTE that Target should never be null when playing // TODO: handle damage to child objects of source/target?
		{
			return;
		}
		Canceled = true;
	}


	public Coroutine Play(IEnumerable<Line> lines, GameObject callbackObject = null, KinematicCharacter target = null, Component sourceMain = null, Sprite sprite = null, Color spriteColor = default, AudioClip sfx = null, bool forceWorldspace = false, int loopIdx = -1, params WeightedObject<Dialogue.Expression>[][] expressionSets)
	{
		if (IsPlaying)
		{
			return null;
		}

		m_revealedCharCount = 0;
		m_lastRevealTime = Time.time;
		m_replyIdx = 0;
		m_queueFollowUp = null;
		m_callbackObject = callbackObject;
		Target = target;
		m_sourceMain = sourceMain;
		m_loopIdx = loopIdx;
		Canceled = false;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		return StartCoroutine(AdvanceDialogue(lines, expressionSets, sfx, forceWorldspace, sprite, spriteColor));
	}

	public void OnReplySelected(GameObject replyObject)
	{
		int idxRaw = replyObject.transform.GetSiblingIndex() - 1; // -1 due to deactivated template object
		Line.Reply[] repliesCur = m_queue.Peek().m_replies;
		m_replyIdx = repliesCur.Count(reply => (idxRaw >= 0 && reply.m_deactivated) || --idxRaw >= 0);
		Line.Reply replyCur = repliesCur?[m_replyIdx];
		if (!string.IsNullOrEmpty(replyCur?.m_eventName))
		{
			SendMessages(replyCur.m_eventName, replyCur);
		}

		if (replyCur.m_relationshipIncrement != 0.0f)
		{
			// TODO: function?
			InteractNpc npcA = Target.GetComponent<InteractNpc>();
			InteractNpc npcB = m_sourceMain.GetComponent<InteractNpc>();
			Debug.Assert(npcA != null || npcB != null); // TODO: support avatar-avatar / enemy-enemy dialogue replies?
			GameController.RelationshipIncrement(npcA == null ? npcB.Index : npcA.Index, npcB == null ? npcA.Index : npcB.Index, replyCur.m_relationshipIncrement); // NOTE the use of "self-relationships" for non-NPC-targeted lines // TODO?
		}

		if (m_queueFollowUp == null && replyCur?.m_followUp != null && replyCur.m_followUp.Length > 0)
		{
			m_queueFollowUp = new(replyCur.m_followUp);
		}
		else
		{
			m_queue.Dequeue();
		}
		m_forceNewLine = true;

		CleanupReplyMenu();
	}


	// TODO: decouple from DialogueController and move elsewhere?
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)")]
	public void MerchantSell(Line.Reply reply)
	{
		if (Target == null)
		{
			return;
		}
		IAttachable[] attachables = Target.GetComponentsInChildren<IAttachable>(true);
		if (attachables.Length <= 0)
		{
			m_queue.Enqueue(new() { m_text = "{merchantSellEmpty.}" });
			m_loopIdx = -1;
			return;
		}

		List<Line.Reply> replyList = new();
		foreach (IAttachable attachable in attachables)
		{
			int savableType = ((ISavable)attachable).Type; // TODO: don't assume all attachables are also savables?
			if (savableType < 0)
			{
				continue; // un-typed "savable", such as a torch
			}
			bool isCriticalPath = attachable is ItemController item && item.IsCriticalPath;
			replyList.Add(new() { m_text = attachable.Name + " - " + GameController.Instance.m_savableFactory.m_savables[savableType].m_materialsProduced + " materials", m_eventName = isCriticalPath ? null : "MerchantDespawn", m_followUp = isCriticalPath ? m_merchantFollowUpCriticalPath : m_merchantFollowUpSell }); // TODO: parameterize whether to show material costs?
		}
		replyList.Add(new() { m_text = "{merchantSellCancel.}", m_followUp = m_merchantFollowUpCancel, m_breakAfterward = true });

		Line line = new() { m_text = "{merchantSellPre.}", m_replies = replyList.ToArray() };
		Debug.Assert(m_queue.Count == 1, "Out-of-order selling dialogue?");
		m_queue.Enqueue(line);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)")]
	public void MerchantDespawn(Line.Reply reply)
	{
		if (Target == null)
		{
			return;
		}

		// record acquisition
		IEnumerable<IAttachable> attachables = Target.GetComponentsInChildren<IAttachable>(true).Where(attachable => ((ISavable)attachable).Type >= 0); // TODO: don't assume all attachables are also savables?
		IAttachable attachable = attachables.ElementAt(m_replyIdx);
		int savableType = ((ISavable)attachable).Type; // TODO: don't assume all attachables are also savables?
		++GameController.MerchantAcquiredCounts[savableType];
		GameController.MerchantMaterials += GameController.Instance.m_savableFactory.m_savables[savableType].m_materialsProduced;

		// detach any children
		Component attachableComp = attachable.Component;
		foreach (IAttachable child in attachableComp.GetComponentsInChildren<IAttachable>(true))
		{
			if (child == attachable)
			{
				continue;
			}
			child.Detach(true);
		}

		// despawn
		if (attachableComp.TryGetComponent(out DespawnEffect despawnEffect))
		{
			Destroy(despawnEffect);
		}
		if (m_sourceMain != null && m_sourceMain.TryGetComponent(out IDespawner despawner))
		{
			despawner.DespawnAttachable(attachable);
		}
		else
		{
			Simulation.Schedule<ObjectDespawn>().m_object = attachableComp.gameObject;
		}
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)
	public void MerchantBuy(Line.Reply reply)
	{
		// TODO: parameterize all expression names?
		m_merchantType = reply.m_userdata; // TODO: use an enum somehow?
		List<Line.Reply> replyList = new();
		switch (m_merchantType)
		{
			case 0: // item merchant
				for (int type = 0, n = GameController.MerchantAcquiredCounts.Length; type < n; ++type)
				{
					if (GameController.MerchantAcquiredCounts[type] < 1)
					{
						continue;
					}
					SavableFactory.SavableInfo savableInfo = GameController.Instance.m_savableFactory.m_savables[type];
					bool enoughMaterials = GameController.MerchantMaterials >= savableInfo.m_materialsConsumed;
					replyList.Add(new() { m_text = savableInfo.m_prefab.GetComponent<IAttachable>().Name + " - " + savableInfo.m_materialsConsumed + " materials", m_eventName = enoughMaterials ? "MerchantSpawn" : null, m_userdata = type, m_followUp = new[] { enoughMaterials ? "{merchantBuyPost.}" : "{merchantBuyInsufficient.}" } });
				}
				break;
			case 1: // customization merchant stage 1
				replyList.Add(new() { m_text = "{merchantBuyFor} myself{.}", m_eventName = "MerchantSetTargetAndBuy", m_userdata = -1, m_followUp = new[] { "{merchantBuyFor} you{?}" } });
				for (int npcI = 0; npcI < GameController.Instance.NpcsTotal; ++npcI)
				{
					Dialogue[] dialogues = GameController.NpcDialogues(npcI);
					if (dialogues.All(dOuter => dOuter.m_dialogue.All(d => d.m_weight >= 0.0f))) // TODO: don't assume all NPCs have single-use introductions?
					{
						continue;
					}
					string[] npcNames = dialogues.SelectMany(d => d.m_expressions).Where(e => e.m_object.m_key == "name").Select(e => e.m_object.m_replacement).ToArray(); // TODO: detect current NPC and replace names w/ "you"/"me"
					replyList.Add(new() { m_text = "{merchantBuyFor} " + npcNames.Random() + "{.}", m_eventName = "MerchantSetTargetAndBuy", m_userdata = npcI, m_followUp = new[] { "{merchantBuyFor} " + npcNames.Random() + "{?}" } });
				}
				// TODO: option for Entryway customization?
				break;
			case 2: // customization merchant stage 2
				for (int type = 0; type < GameController.Instance.m_npcClothing.Length; ++type)
				{
					WeightedObject<SavableFactory.SavableInfo> clothingInfo = GameController.Instance.m_npcClothing[type];
					if (clothingInfo.m_object.m_prefab == null)
					{
						continue;
					}
					bool enoughMaterials = GameController.MerchantMaterials >= clothingInfo.m_object.m_materialsConsumed;
					replyList.Add(new() { m_text = clothingInfo.m_object.m_prefab.GetComponent<ClothController>().m_name + " - " + clothingInfo.m_object.m_materialsConsumed + " materials", m_eventName = enoughMaterials ? "MerchantSpawn" : null, m_userdata = type, m_followUp = new[] { enoughMaterials ? (m_merchantTargetIdx == -1 ? "{merchantBuyPost.}" : "{merchantBuyPost2.}") : "{merchantBuyInsufficient.}" } });
				}
				break;
			default:
				Debug.LogError("Unhandled MerchantBuy type?");
				break;
		}

		if (replyList.Count <= 0)
		{
			m_queue.Enqueue(new() { m_text = "{merchantBuyEmpty.}" });
			return;
		}

		replyList.Add(new() { m_text = "{merchantBuyCancel.}", m_followUp = new[] { "{merchantBuyCanceled.}" }, m_breakAfterward = true });

		m_queue.Enqueue(new() { m_text = m_merchantType == 1 ? "{merchantBuyPre1.}" : "{merchantBuyPre.} {merchantMaterialsPre} " + GameController.MerchantMaterials + " {merchantMaterialsPost.}", m_replies = replyList.ToArray() }); // TODO: tie MerchantMaterials to a dynamic expression?
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)
	public void MerchantSetTargetAndBuy(Line.Reply reply)
	{
		m_merchantTargetIdx = reply.m_userdata;
		reply.m_userdata = m_merchantType + 1;
		MerchantBuy(reply);
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)
	public void MerchantSpawn(Line.Reply reply)
	{
		if (m_merchantTargetIdx < 0 && Target == null)
		{
			return;
		}
		SavableFactory.SavableInfo[] savables = m_merchantType == 0 ? GameController.Instance.m_savableFactory.m_savables : GameController.Instance.m_npcClothing.Select(weightedObj => weightedObj.m_object).ToArray(); // TODO: efficiency?
		int savableType = reply.m_userdata;
		int cost = savables[savableType].m_materialsConsumed;
		Debug.Assert(GameController.MerchantMaterials >= cost);

		KinematicCharacter targetFinal = m_merchantTargetIdx < 0 ? Target : null;
		if (targetFinal == null)
		{
			GameController.Instance.NpcAddClothing(m_merchantTargetIdx, savableType);
		}
		else
		{
			GameObject savableObj = savables == GameController.Instance.m_savableFactory.m_savables ? GameController.Instance.m_savableFactory.Instantiate(savableType, targetFinal.transform.position, Quaternion.identity).Component.gameObject : Instantiate(savables[savableType].m_prefab, targetFinal.transform);
			if (savableObj.transform.parent == null && savableObj.TryGetComponent(out IAttachable attachable))
			{
				targetFinal.ChildAttach(attachable);
			}
		}
		GameController.MerchantMaterials -= cost;
	}


	private System.Collections.IEnumerator AdvanceDialogue(IEnumerable<Line> linesOrig, WeightedObject<Dialogue.Expression>[][] expressionSets, AudioClip sfx, bool forceWorldspace, Sprite sprite, Color color)
	{
		m_text.text = null;
		m_queue = new(linesOrig);
		m_audio.clip = sfx;

		// character/controls setup
		if (Target == null)
		{
			m_canvas.enabled = false; // don't show dialogue box until we're ready
			yield return new WaitUntil(() => GameController.Instance.m_avatars.Count > 0);
			Target = GameController.Instance.m_avatars.First(); // TODO: don't assume that the first avatar will always remain?
			m_canvas.enabled = true;
		}
		AvatarController avatar = Target as AvatarController;
		bool isWorldspace = forceWorldspace || avatar == null;
		if (!isWorldspace)
		{
			avatar.ControlsUI.Enable();
		}
		InputAction submitKey = isWorldspace ? null : avatar.Controls.actions["Submit"];
		m_submitReleasedSinceNewline = false;

		// walk-away prevention
		// TODO: also include non-primary sources after speaking? allow limited functionality/movement?
		bool npcSetPassive(Component c, bool passive)
		{
			if (c == null)
			{
				return false;
			}
			if (c.TryGetComponent(out AIController ai))
			{
				bool wasPassive = ai.m_passive;
				ai.m_passive = passive;
				return wasPassive != passive;
			}
			return false;
		}
		bool setPassive = false;
		if (!isWorldspace)
		{
			setPassive = npcSetPassive(m_sourceMain, true);
		}

		// canvas setup
		void setBackdropSize(float offsetMag)
		{
			m_canvasBackdropTf.offsetMin = new(offsetMag, m_canvasBackdropTf.offsetMin.y);
			m_canvasBackdropTf.offsetMax = new(-offsetMag, m_canvasBackdropTf.offsetMax.y);
		}
		void fitBackdropToText()
		{
			if (isWorldspace)
			{
				// TODO: more accurate width to prevent thrashing between single- and double-lines
				float widthAvailable = (m_canvasTf.sizeDelta.x - m_textTf.offsetMin.x) * 0.5f;
				float textWidthMin = widthAvailable - m_textTf.offsetMin.x * 0.5f; // TODO: parameterize?
				float offsetMag = m_canvasOffsetOrig + Mathf.Clamp(widthAvailable - m_text.preferredWidth * 0.5f, 0.0f, textWidthMin);
				setBackdropSize(offsetMag);
			}
			else
			{
				setBackdropSize(m_canvasOffsetOrig);
			}
		}
		m_canvas.renderMode = isWorldspace ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
		m_canvas.sortingLayerID = isWorldspace ? SortingLayer.NameToID(m_worldspaceLayerName) : m_canvasLayerOrig; // to prevent worldspace canvas visibility through Exterior objects // TODO: cache worldspace layer ID?
		RectTransform rectTf = m_canvas.GetComponent<RectTransform>();
		float scale = isWorldspace ? m_worldspaceWidth / rectTf.sizeDelta.x : 1.0f;
		m_canvas.transform.localScale = new(scale, scale, scale);
		fitBackdropToText();
		rectTf.pivot = new(0.5f, isWorldspace ? 0.0f : 0.5f);
		m_canvas.GetComponent<AudioSource>().spatialBlend = isWorldspace ? 1.0f : 0.0f;
		Transform followTf = isWorldspace ? m_sourceMain.transform : null;

		WaitUntil replyWait = new(() => !m_replyMenu.gameObject.activeInHierarchy || Canceled);
		int tagCharCount = 0;

		m_forceNewLine = true;
		Line lineCur = null;
		string textCur = null;
		int textCurLen = -1;

		while (m_queue.Count > 0 && !Canceled)
		{
			// NOTE that we don't use transform parenting/attachment to avoid destruction if character dies
			// TODO: efficiency?
			if (isWorldspace && followTf != null)
			{
				m_canvas.transform.position = new(followTf.position.x, followTf.GetComponent<Collider2D>().bounds.max.y + m_worldspaceSpacing, followTf.position.z);
			}

			// handle input for reply selection
			if (m_replyMenu.gameObject.activeInHierarchy)
			{
				// wait for OnReplySelected()
				yield return replyWait;
				continue;
			}

			// maybe move to next line
			bool stillRevealing = m_revealedCharCount + tagCharCount < textCurLen;
			if (m_forceNewLine || lineCur == null || textCur == null || (((submitKey == null && !ConsoleCommands.PassiveAI) || (submitKey != null && submitKey.WasPressedThisFrame())) && !stillRevealing && m_lastRevealTime + m_newlineSecondsMin <= Time.time))
			{
				// next line
				m_continueIndicator.SetActive(false);
				if (!m_forceNewLine)
				{
					bool wasInFollowUp = m_queueFollowUp != null;
					if (m_queueFollowUp != null && m_queueFollowUp.Count <= 1)
					{
						m_queueFollowUp = null;
					}
					if (m_queueFollowUp != null)
					{
						m_queueFollowUp.Dequeue();
					}
					else if (wasInFollowUp && lineCur.m_replies[m_replyIdx].m_breakAfterward)
					{
						break;
					}
					else
					{
						m_queue.Dequeue();
					}
				}
				lineCur = CurrentLine(out textCur, out textCurLen, ref followTf, expressionSets, sprite, color);
				m_revealedCharCount = 0;
				m_lastRevealTime = Time.time;
				m_submitReleasedSinceNewline = false;
				m_forceNewLine = false;
				stillRevealing = true;
				if (lineCur == null || textCur == null)
				{
					continue;
				}
			}
			bool submitPressed = submitKey != null && submitKey.IsPressed();
			m_submitReleasedSinceNewline = m_submitReleasedSinceNewline || !submitPressed;

			// maybe reveal next letter(s)
			float revealDurationCur = stillRevealing && m_submitReleasedSinceNewline && submitPressed ? m_revealSecondsFast : m_revealSeconds;
			float nextRevealTime = m_lastRevealTime + revealDurationCur;
			if (stillRevealing && nextRevealTime <= Time.time)
			{
				// reveal next letter(s)
				int numToReveal = (int)((Time.time - nextRevealTime) / revealDurationCur) + 1;
				m_revealedCharCount = Math.Min(m_revealedCharCount + numToReveal, textCurLen - tagCharCount);
				m_lastRevealTime += revealDurationCur * numToReveal;

				// ensure matching tags
				// TODO: only parse newly added characters?
				tagCharCount = 0;
				List<string> endTags = new();
				MatchCollection matches = m_tagMatcher.Matches(textCur);
				foreach (Match match in matches.Cast<Match>())
				{
					Debug.Assert(match.Success);
					if (match.Index < m_revealedCharCount + tagCharCount)
					{
						string endTag = "</" + match.Groups[1] + ">";
						tagCharCount += endTag.Length - 1; // -1 due to '/' not being contained in opening tag
						if (match.Index + match.Length - tagCharCount - endTag.Length >= m_revealedCharCount)
						{
							endTags.Add(endTag);
						}
						else
						{
							tagCharCount += endTag.Length;
						}
					}
				}
				endTags.Reverse(); // since closing tags should be in reverse order of the opening tags

				// update UI
				int lenCur = Mathf.Min(textCur.Length, m_revealedCharCount + tagCharCount); // TODO: determine why this needs to be clamped sometimes?
				m_text.text = textCur == null ? null : textCur[0 .. lenCur] + endTags.Aggregate("", (a, b) => a + b);
				fitBackdropToText();

				// SFX
				int indexCur = lenCur - 1;
				if (m_audio.clip != null && !char.IsWhiteSpace(textCur, indexCur) && !char.IsPunctuation(textCur, indexCur))
				{
					m_audio.PlayOneShot(m_audio.clip);
				}

				if (m_queue.Count > 0 && lenCur >= textCurLen)
				{
					m_text.ForceMeshUpdate(); // since we need the updated bounds

					// display any replies
					CleanupReplyMenu(); // just in case of leftovers from previous replies // NOTE that this has to be BEFORE m_replyMenu is activated
					bool active = m_queueFollowUp == null && lineCur.m_replies != null && lineCur.m_replies.Length > 0;
					m_replyMenu.gameObject.SetActive(active);
					if (active)
					{
						TMP_Text newText = null;
						float yMargin = -1.0f;
						float yOffsetCur = 0.0f;
						for (int i = 0, n = lineCur.m_replies.Length; i < n; ++i)
						{
							// check precondition
							Line.Reply replyCur = lineCur.m_replies[i];
							if (!string.IsNullOrEmpty(replyCur.m_preconditionName))
							{
								SendMessages(replyCur.m_preconditionName, replyCur);
							}
							if (replyCur.m_deactivated)
							{
								continue;
							}

							// check relationship
							if (replyCur.m_relationshipMin > 0.0f || replyCur.m_relationshipMax < 1.0f)
							{
								// TODO: function?
								InteractNpc npcA = Target.GetComponent<InteractNpc>();
								InteractNpc npcB = m_sourceMain.GetComponent<InteractNpc>();
								Debug.Assert(npcA != null || npcB != null); // TODO: support avatar-avatar / enemy-enemy dialogue replies?
								float relationshipPctCur = GameController.RelationshipPercent(npcA == null ? npcB.Index : npcA.Index, npcB == null ? npcA.Index : npcB.Index); // NOTE the use of "self-relationships" for non-NPC-targeted lines // TODO?
								if (relationshipPctCur < replyCur.m_relationshipMin || relationshipPctCur > replyCur.m_relationshipMax)
								{
									continue;
								}
							}

							GameObject newObj = Instantiate(m_replyTemplate, ReplyParentTf);
							newText = newObj.GetComponentInChildren<TMP_Text>();
							newText.text = ReplaceExpressions(replyCur.m_text, expressionSets);
							RectTransform newTf = newObj.GetComponent<RectTransform>();
							if (yMargin == -1.0f)
							{
								yMargin = Mathf.Abs(newTf.anchoredPosition.y);
								UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(newObj);
							}
							newTf.anchoredPosition += new Vector2(0.0f, yOffsetCur);
							newObj.SetActive(true);
							yOffsetCur -= newTf.sizeDelta.y;
						}

						// set background sizes to fit and scroll if necessary
						RectTransform scrollTf = (RectTransform)ReplyParentTf;
						scrollTf.sizeDelta = new(scrollTf.sizeDelta.x, Mathf.Abs(yOffsetCur) + yMargin * 2.0f);
						RectTransform menuTf = (RectTransform)m_replyMenu;
						menuTf.sizeDelta = new(menuTf.sizeDelta.x, Mathf.Min(scrollTf.sizeDelta.y + Mathf.Abs(((RectTransform)menuTf.GetChild(0)).sizeDelta.y), Screen.height - menuTf.anchoredPosition.y));
					}
					else if (!isWorldspace)
					{
						// display continue indicator
						Extents lineExtents = m_text.textInfo.lineInfo[m_text.textInfo.lineCount - 1].lineExtents; // NOTE that lineInfo.Last() may be stale info
						m_continueIndicator.GetComponent<RectTransform>().anchoredPosition = new(lineExtents.max.x + m_indicatorSpacing, lineExtents.min.y + m_indicatorSpacing);
						m_continueIndicator.SetActive(true);
					}
				}
			}

			yield return null; // TODO: don't process every frame w/o losing responsiveness?

			if (m_loopIdx >= 0 && m_queue.Count <= 0)
			{
				m_queue = new(linesOrig.Skip(m_loopIdx));
			}
		}

		if (!isWorldspace)
		{
			avatar.ControlsUI.Disable(); // TODO: check for other UI?
		}
		CleanupReplyMenu(); // in case of reply cancellation via damage/etc.
		m_continueIndicator.SetActive(false); // in case of reply cancellation via damage/etc.
		gameObject.SetActive(false);
		if (setPassive)
		{
			npcSetPassive(m_sourceMain, false);
		}
		Target = null;
		m_merchantType = -1;
		m_merchantTargetIdx = -1;

		m_dialogueTimePrevious = Time.time;
	}

	private Line CurrentLine(out string text, out int textLen, ref Transform followTf, WeightedObject<Dialogue.Expression>[][] expressionSets, Sprite spriteDefault, Color colorDefault)
	{
		Line line = m_queue.Count > 0 ? m_queue.Peek() : null;
		text = m_queueFollowUp != null ? m_queueFollowUp.Peek() : line?.m_text;

		if (!string.IsNullOrEmpty(text))
		{
			text = ReplaceExpressions(text, expressionSets);
		}

		// update image if necessary
		if (line?.m_source == null)
		{
			m_image.sprite = spriteDefault;
			m_image.color = colorDefault;
			followTf = m_sourceMain != null ? m_sourceMain.transform : Target != null ? Target.transform : null;
		}
		else
		{
			InteractNpc sourceInteract = FindObjectsOfType<InteractNpc>().FirstOrDefault(interact => GameController.NpcDialogues(interact.Index).Contains(line.m_source) && Vector2.Distance(m_sourceMain.transform.position, interact.transform.position) <= line.m_sourceDistMax); // NOTE that we can't just use m_sourceMain since some dialogues reference third parties
			if (sourceInteract == null)
			{
				// if the source is not present, end the dialogue // TODO: parameterize?
				text = null;
				line = null;
				m_queue.Clear();
			}
			else
			{
				m_image.sprite = sourceInteract.m_dialogueSprite;
				m_image.color = sourceInteract.GetComponent<SpriteRenderer>().color;
				followTf = sourceInteract.transform;
			}
		}
		m_image.enabled = m_image.sprite != null;

		textLen = text != null ? text.Length : 0;

		return line;
	}

	private static string ReplaceExpressions(string text, WeightedObject<Dialogue.Expression>[][] expressionSets)
	{
		if (expressionSets == null)
		{
			return text;
		}

		int setIdx = 0;
		foreach (IEnumerable<Dialogue.Expression> expressionSet in expressionSets.Select(set => set?.RandomWeightedOrder()))
		{
			bool foundReplacement = false; // NOTE that this isn't strictly necessary, but safeguards against infinite looping
			do
			{
				foundReplacement = false;
				foreach (Dialogue.Expression expression in expressionSet)
				{
					string keyBracketed = "{" + expression.m_key + (setIdx > 0 ? setIdx.ToString() : "") + "}";
					if (text.Contains(keyBracketed)) // TODO: reduce redundant searching?
					{
						foundReplacement = true;
						text = text.ReplaceFirst(keyBracketed, expression.m_replacement);
					}
				}
			}
			while (foundReplacement && text.Contains('{')); // NOTE that we have to allow looping over expressionSet multiple times since expression replacements can contain keys from earlier in the list
			++setIdx;
		}

		if (string.IsNullOrEmpty(text)) // NOTE that we handle empty replacements for cases such as ending a dialogue w/o further reply
		{
			return null;
		}

		// remove commas/spaces made unnecessary by blank expressions
		text = m_commaSpaceRemovalMatcher.Replace(text, "");

		// auto-capitalize
		char[] textArray = text.Trim().ToCharArray();
		textArray[0] = char.ToUpper(textArray.First());
		for (int i = 2; i < textArray.Length; ++i)
		{
			char c = textArray[i - 2];
			if (((c == '.' && (i < 3 || textArray[i - 3] != '.')) || c == '?' || c == '!') && char.IsWhiteSpace(textArray[i - 1])) // NOTE the extra logic to avoid capitalizing after ellipses
			{
				textArray[i] = char.ToUpper(textArray[i]);
			}
		}
		text = new(textArray);

		return text;
	}

	private void SendMessages(string functionName, object value)
	{
		// TODO: efficiency? error if none of the objects finds a receiver?
		gameObject.SendMessage(functionName, value, SendMessageOptions.DontRequireReceiver);
		if (m_callbackObject != null)
		{
			m_callbackObject.SendMessage(functionName, value, SendMessageOptions.DontRequireReceiver);
		}
		if (Target != null)
		{
			Target.gameObject.SendMessage(functionName, value, SendMessageOptions.DontRequireReceiver);
		}
		GameController.Instance.gameObject.SendMessage(functionName, value, SendMessageOptions.DontRequireReceiver);
	}

	private void CleanupReplyMenu()
	{
		m_replyMenu.gameObject.SetActive(false);

		Transform parentTf = ReplyParentTf;
		for (int i = 0, n = parentTf.childCount; i < n; ++i)
		{
			GameObject child = parentTf.GetChild(i).gameObject;
			if (child == m_replyTemplate)
			{
				continue;
			}
			Simulation.Schedule<ObjectDespawn>().m_object = child;
		}
	}
}
