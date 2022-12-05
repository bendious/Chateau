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
	[SerializeField] private UnityEngine.UI.Image m_image;
	[SerializeField] private TMP_Text m_text;
	[SerializeField] private GameObject m_continueIndicator;
	public Transform m_replyMenu;
	[SerializeField] private GameObject m_replyTemplate;

	[SerializeField] private float m_worldspaceWidth = 8.0f;

	[SerializeField] private float m_revealSeconds = 0.05f;
	[SerializeField] private float m_revealSecondsFast = 0.005f;

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


	private Transform ReplyParentTf => m_replyTemplate.transform.parent;


	private static readonly Regex m_tagMatcher = new(@"<(.+)>.*?</\1>"); // NOTE the lazy rather than greedy wildcard matching to prevent multiple sets of identical tags being combined into one group // TODO: handle identical nested tags?

	private AudioSource m_audio;
	private Canvas m_canvas;

	private int m_canvasLayerOrig;

	private Queue<Line> m_queue;
	private int m_revealedCharCount;
	private float m_lastRevealTime;
	private int m_replyIdx;
	private Queue<string> m_queueFollowUp;
	private GameObject m_callbackObject;
	public KinematicCharacter Character { get; private set; }
	private int m_loopIdx = -1;

	private bool m_forceNewLine = false;


	private void Awake()
	{
		m_audio = GetComponent<AudioSource>();
		m_canvas = GetComponent<Canvas>();
		m_canvasLayerOrig = m_canvas.sortingLayerID;
	}


	public Coroutine Play(IEnumerable<Line> lines, GameObject callbackObject = null, KinematicCharacter character = null, Sprite sprite = null, Color spriteColor = default, AudioClip sfx = null, int loopIdx = -1, params WeightedObject<Dialogue.Expression>[][] expressionSets)
	{
		m_revealedCharCount = 0;
		m_lastRevealTime = Time.time;
		m_replyIdx = 0;
		m_queueFollowUp = null;
		m_callbackObject = callbackObject;
		Character = character;
		m_loopIdx = loopIdx;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		return StartCoroutine(AdvanceDialogue(lines, expressionSets, sfx, sprite, spriteColor));
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

		if (m_queueFollowUp == null && replyCur?.m_followUp != null && replyCur.m_followUp.Length > 0)
		{
			m_queueFollowUp = new(replyCur.m_followUp);
			m_revealedCharCount = 0;
			m_lastRevealTime = Time.time;
		}
		else
		{
			m_forceNewLine = true;
		}
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


	// TODO: decouple from DialogueController and move elsewhere?
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)")]
	public void MerchantSell(Line.Reply reply)
	{
		IAttachable[] attachables = Character.GetComponentsInChildren<IAttachable>(true);
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
		// record acquisition
		IEnumerable<IAttachable> attachables = Character.GetComponentsInChildren<IAttachable>(true).Where(attachable => ((ISavable)attachable).Type >= 0); // TODO: don't assume all attachables are also savables?
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
		DespawnEffect despawnEffect = attachableComp.GetComponent<DespawnEffect>();
		if (despawnEffect != null)
		{
			Destroy(despawnEffect);
		}
		Simulation.Schedule<ObjectDespawn>().m_object = attachableComp.gameObject;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)")]
	public void MerchantBuy(Line.Reply reply)
	{
		// TODO: move hardcoded text into data?
		List<Line.Reply> replyList = new();
		for (int type = 0, n = GameController.MerchantAcquiredCounts.Length; type < n; ++type)
		{
			if (GameController.MerchantAcquiredCounts[type] < 1)
			{
				continue;
			}
			SavableFactory.SavableInfo savableInfo = GameController.Instance.m_savableFactory.m_savables[type];
			bool enoughMaterials = GameController.MerchantMaterials >= savableInfo.m_materialsConsumed;
			replyList.Add(new() { m_text = savableInfo.m_prefab.GetComponent<IAttachable>().Name + " - " + savableInfo.m_materialsConsumed + " materials", m_eventName = enoughMaterials ? "MerchantSpawn" : null, m_userdata = type, m_followUp = new[] { enoughMaterials ? "Here you go!" : "Hmm, I'll need more materials for that." } });
		}

		replyList.Add(new() { m_text = "Nothing, thanks.", m_followUp = new[] { "{denied.} {interjection} you know where to find me." }, m_breakAfterward = true });

		m_queue.Enqueue(new() { m_text = "What'd ya have in mind? We've got " + GameController.MerchantMaterials + " materials to work with.", m_replies = replyList.ToArray() });
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)
	public void MerchantSpawn(Line.Reply reply)
	{
		int savableType = reply.m_userdata;
		int cost = GameController.Instance.m_savableFactory.m_savables[savableType].m_materialsConsumed;
		Debug.Assert(GameController.MerchantMaterials >= cost);
		ISavable savable = GameController.Instance.m_savableFactory.Instantiate(savableType, Character.transform.position, Quaternion.identity);
		Character.ChildAttach(savable.Component.GetComponent<IAttachable>()); // TODO: don't assume all savables are also attachable?
		GameController.MerchantMaterials -= cost;
	}


	private System.Collections.IEnumerator AdvanceDialogue(IEnumerable<Line> linesOrig, WeightedObject<Dialogue.Expression>[][] expressionSets, AudioClip sfx, Sprite sprite, Color color)
	{
		m_text.text = null;
		m_queue = new(linesOrig);
		Dialogue.Expression[][] expressionSetsOrdered = expressionSets?.Select(set => set?.RandomWeightedOrder().ToArray()).ToArray(); // NOTE the conversion to an array to prevent IEnumerable re-calculating w/ each access // TODO: re-order after each line?
		m_audio.clip = sfx;

		// character/controls setup
		if (Character == null)
		{
			m_canvas.enabled = false; // don't show dialogue box until we're ready
			yield return new WaitUntil(() => GameController.Instance.m_avatars.Count > 0);
			Character = GameController.Instance.m_avatars.First(); // TODO: don't assume that the first avatar will always remain?
			m_canvas.enabled = true;
		}
		AvatarController avatar = Character as AvatarController;
		if (avatar != null)
		{
			avatar.ControlsUI.Enable();
		}
		bool isWorldspace = avatar == null;
		InputAction submitKey = isWorldspace ? null : avatar.Controls.actions["Submit"];
		bool submitReleasedSinceNewline = true;

		// canvas setup
		m_canvas.renderMode = isWorldspace ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
		m_canvas.sortingLayerID = isWorldspace ? 0 : m_canvasLayerOrig; // to prevent worldspace canvas visibility through Exterior objects // TODO: don't assume the default layer is always the desired worldspace layer?
		RectTransform rectTf = m_canvas.GetComponent<RectTransform>();
		float scale = isWorldspace ? m_worldspaceWidth / rectTf.sizeDelta.x : 1.0f;
		m_canvas.transform.localScale = new(scale, scale, scale);
		rectTf.pivot = new(0.5f, isWorldspace ? 0.0f : 0.5f);
		m_canvas.GetComponent<AudioSource>().spatialBlend = isWorldspace ? 1.0f : 0.0f;
		Transform followTf = isWorldspace ? Character.transform : null;

		WaitUntil replyWait = new(() => !m_replyMenu.gameObject.activeInHierarchy);
		int tagCharCount = 0;

		while (m_queue.Count > 0)
		{
			// NOTE that we don't use transform parenting/attachment to avoid destruction if character dies
			// TODO: efficiency?
			if (isWorldspace && followTf != null)
			{
				m_canvas.transform.position = new(followTf.position.x, followTf.GetComponent<Collider2D>().bounds.max.y, followTf.position.z);
			}

			// handle input for reply selection
			if (m_replyMenu.gameObject.activeInHierarchy)
			{
				// wait for OnReplySelected()
				yield return replyWait;
				continue;
			}

			// current state
			// TODO: don't redo every time?
			Line lineCur = NextLine(out string textCur, out int textCurLen, ref followTf, expressionSetsOrdered, sprite, color);

			// maybe move to next line
			bool stillRevealing = m_revealedCharCount + tagCharCount < textCurLen;
			if (m_forceNewLine || ((submitKey == null || submitKey.WasPressedThisFrame()) && !stillRevealing)) // TODO: non-avatar delay before newline
			{
				// next line
				m_continueIndicator.SetActive(false);
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
					lineCur = NextLine(out textCur, out textCurLen, ref followTf, expressionSetsOrdered, sprite, color);
					if (lineCur == null)
					{
						break;
					}
				}
				m_revealedCharCount = 0;
				m_lastRevealTime = Time.time;
				m_forceNewLine = false;
				submitReleasedSinceNewline = false;
				stillRevealing = true;
			}
			bool submitPressed = submitKey != null && submitKey.IsPressed();
			submitReleasedSinceNewline = submitReleasedSinceNewline || !submitPressed;

			// maybe reveal next letter(s)
			float revealDurationCur = stillRevealing && submitReleasedSinceNewline && submitPressed ? m_revealSecondsFast : m_revealSeconds;
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
				foreach (Match match in matches)
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
				m_text.text = textCur == null ? null : textCur[0 .. (m_revealedCharCount + tagCharCount)] + endTags.Aggregate("", (a, b) => a + b);

				// SFX
				int indexCur = m_revealedCharCount + tagCharCount - 1;
				if (m_audio.clip != null && !char.IsWhiteSpace(textCur, indexCur) && !char.IsPunctuation(textCur, indexCur))
				{
					m_audio.PlayOneShot(m_audio.clip);
				}

				if (m_queue.Count > 0 && m_revealedCharCount + tagCharCount >= textCurLen)
				{
					yield return null; // to allow TMP to catch up with us & calculate bounds

					// display any replies
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

							GameObject newObj = Instantiate(m_replyTemplate, ReplyParentTf);
							newText = newObj.GetComponentInChildren<TMP_Text>();
							newText.text = ReplaceExpressions(replyCur.m_text, expressionSetsOrdered);
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
					else
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

		if (avatar != null)
		{
			avatar.ControlsUI.Disable(); // TODO: check for other UI?
		}
		gameObject.SetActive(false);
	}

	private Line NextLine(out string text, out int textLen, ref Transform followTf, Dialogue.Expression[][] expressionSetsOrdered, Sprite spriteDefault, Color colorDefault)
	{
		Line line = m_queue.Count > 0 ? m_queue.Peek() : null;
		text = m_queueFollowUp != null ? m_queueFollowUp.Peek() : line?.m_text;

		if (!string.IsNullOrEmpty(text))
		{
			text = ReplaceExpressions(text, expressionSetsOrdered);

			if (!string.IsNullOrEmpty(text)) // NOTE that we handle empty replacements even though we generally don't want to end up w/ an empty string
			{
				// compress double spaces to support blank expressions
				text = text.Replace("  ", " ");

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
			}
		}

		// update image if necessary
		if (line?.m_source == null)
		{
			m_image.sprite = spriteDefault;
			m_image.color = colorDefault;
			followTf = Character == null ? null : Character.transform;
		}
		else
		{
			InteractNpc sourceInteract = FindObjectsOfType<InteractNpc>().FirstOrDefault(interact => GameController.NpcDialogues(interact.Index).Contains(line.m_source) && Vector2.Distance(Character.transform.position, interact.transform.position) <= line.m_sourceDistMax);
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

	private static string ReplaceExpressions(string text, Dialogue.Expression[][] expressionSetsOrdered)
	{
		if (expressionSetsOrdered == null)
		{
			return text;
		}

		bool foundReplacement = false; // NOTE that this isn't strictly necessary, but safeguards against infinite looping
		int setIdx = 0;
		foreach (Dialogue.Expression[] expressionSet in expressionSetsOrdered)
		{
			if (expressionSet != null)
			{
				do
				{
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
			}
			++setIdx;
		}

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
		Character.gameObject.SendMessage(functionName, value, SendMessageOptions.DontRequireReceiver);
		GameController.Instance.gameObject.SendMessage(functionName, value, SendMessageOptions.DontRequireReceiver);
	}
}
