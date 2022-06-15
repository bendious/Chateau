using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;


[DisallowMultipleComponent]
public class DialogueController : MonoBehaviour
{
	public UnityEngine.UI.Image m_image;
	public TMP_Text m_text;
	public GameObject m_continueIndicator;
	[SerializeField] private GameObject m_replyTemplate;

	public float m_revealSeconds = 0.1f;
	public float m_revealSecondsFast = 0.01f;

	public float m_indicatorSpacing = 7.5f;


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
		public Reply[] m_replies;
	}


	public bool IsPlaying => gameObject.activeSelf;


	private Line[] m_linesOrig;
	private Queue<Line> m_queue;
	private int m_revealedCharCount;
	private float m_lastRevealTime;
	private int m_replyIdx;
	private Queue<string> m_queueFollowUp;
	private AvatarController m_avatar;
	private bool m_loop;

	private bool m_forceNewLine = false;


	public void Play(Sprite sprite, Color spriteColor, Line[] lines, AvatarController avatar, bool loop = false, Action postDialogue = null)
	{
		m_image.enabled = sprite != null;
		m_image.sprite = sprite;
		m_image.color = spriteColor;

		m_linesOrig = lines;
		m_queue = new(m_linesOrig);
		m_revealedCharCount = 0;
		m_lastRevealTime = Time.time;
		m_replyIdx = 0;
		m_queueFollowUp = null;
		m_avatar = avatar;
		m_loop = loop;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		StartCoroutine(AdvanceDialogue(postDialogue));
	}

	public void OnReplySelected(GameObject replyObject)
	{
		int idxRaw = replyObject.transform.GetSiblingIndex() - 1; // -1 due to deactivated template object
		Line.Reply[] repliesCur = m_queue.Peek().m_replies;
		m_replyIdx = repliesCur.Count(reply => (idxRaw >= 0 && reply.m_deactivated) || --idxRaw >= 0);
		Line.Reply replyCur = repliesCur?[m_replyIdx];
		if (!string.IsNullOrEmpty(replyCur?.m_eventName))
		{
			gameObject.SendMessage(replyCur.m_eventName);
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
		m_replyTemplate.transform.parent.gameObject.SetActive(false);
		for (int i = 0, n = m_replyTemplate.transform.parent.childCount; i < n; ++i)
		{
			GameObject child = m_replyTemplate.transform.parent.GetChild(i).gameObject;
			if (child == m_replyTemplate)
			{
				continue;
			}
			Simulation.Schedule<ObjectDespawn>().m_object = child;
		}
	}

	public void EnemyTypeHasSpawned(Line.Reply reply)
	{
		reply.m_deactivated = !GameController.Instance.EnemyTypeHasSpawned(reply.m_userdata);
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName)
	public void MerchantSell()
	{
		IAttachable[] attachables = m_avatar.GetComponentsInChildren<IAttachable>(true);
		if (attachables.Length <= 0)
		{
			m_queue.Enqueue(new Line { m_text = "No you don't..." });
			m_loop = false;
			return;
		}

		List<Line.Reply> replyList = new();
		foreach (IAttachable attachable in attachables)
		{
			ItemController item = attachable as ItemController; // TODO: IAttachable.name?
			replyList.Add(new Line.Reply { m_text = item == null ? "Backpack"/*?*/ : item.m_tooltip.Split('\n').First(), m_eventName = "MerchantDespawn", m_followUp = new string[] { "Thank you kindly! Rest assured I won't forget this." } });
		}
		replyList.Add(new Line.Reply { m_text = "Not now.", m_followUp = new string[] { "I see. Well, don't forget to come back if you change your mind." }, m_breakAfterward = true }); // TODO: variance?

		Line line = new() { m_text = "Mind if I take one off your hands?", m_replies = replyList.ToArray() };
		Debug.Assert(m_queue.Count == 1, "Out-of-order selling dialogue?");
		m_queue.Enqueue(line);
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName)
	public void MerchantDespawn()
	{
		// record acquisition
		IAttachable[] attachables = m_avatar.GetComponentsInChildren<IAttachable>(true);
		IAttachable attachable = attachables[m_replyIdx];
		++GameController.MerchantAcquiredCounts[((ISavable)attachable).Type]; // TODO: don't assume all attachables are also savables?

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

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName)
	public void MerchantBuy()
	{
		List<Line.Reply> replyList = new();
		for (int type = 0, n = GameController.MerchantAcquiredCounts.Length; type < n; ++type)
		{
			if (GameController.MerchantAcquiredCounts[type] < 1) // TODO: variable acquisition threshold? fungible "materials" currency?
			{
				continue;
			}
			replyList.Add(new Line.Reply { m_text = GameController.Instance.m_savableFactory.m_savables[type].name, m_eventName = "MerchantSpawn", m_userdata = type, m_followUp = new string[] { "Here you go!" } }); // TODO: use ItemController.m_tooltip?
		}

		replyList.Add(new Line.Reply { m_text = "Nothing, thanks.", m_followUp = new string[] { "Oh, okay. Well, you know where to find me." }, m_breakAfterward = true });

		m_queue.Enqueue(new Line { m_text = "What'll it be?", m_replies = replyList.ToArray() });
	}

	// called via OnReplySelected()/SendMessage(Line.Reply.m_eventName)
	public void MerchantSpawn()
	{
		ISavable savable = GameController.Instance.m_savableFactory.Instantiate(m_queue.Peek().m_replies[m_replyIdx].m_userdata, m_avatar.transform.position, Quaternion.identity);
		m_avatar.ChildAttach(savable.Component.GetComponent<IAttachable>()); // TODO: don't assume all savables are also attachable?
	}


	private System.Collections.IEnumerator AdvanceDialogue(Action postDialogue)
	{
		m_text.text = null;

		// avatar setup
		if (m_avatar == null)
		{
			yield return new WaitUntil(() => GameController.Instance.m_avatars.Count > 0);
			m_avatar = GameController.Instance.m_avatars.First(); // TODO: don't assume that the first avatar will always remain?
		}
		m_avatar.Controls.SwitchCurrentActionMap("UI"); // TODO: un-hardcode?
		InputAction submitKey = m_avatar.Controls.actions["Submit"];

		while (m_queue.Count > 0)
		{
			// handle input for reply selection
			if (m_replyTemplate.transform.parent.gameObject.activeInHierarchy)
			{
				// wait for OnReplySelected()
				WaitUntil waitCondition = new(() => !m_replyTemplate.transform.parent.gameObject.activeInHierarchy);
				yield return waitCondition;
				continue;
			}

			// current state
			Line lineCur = m_queue.Peek();
			string textCur = m_queueFollowUp != null ? m_queueFollowUp.Peek() : lineCur.m_text;
			int textCurLen = textCur.Length;

			// maybe move to next line
			bool stillRevealing = m_revealedCharCount < textCurLen;
			if (m_forceNewLine || (submitKey.WasPressedThisFrame() && !stillRevealing))
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
					lineCur = m_queue.Count > 0 ? m_queue.Peek() : null;
					textCur = lineCur?.m_text;
					textCurLen = textCur != null ? textCur.Length : 0;
				}
				m_revealedCharCount = 0;
				m_lastRevealTime = Time.time;
				m_forceNewLine = false;
				stillRevealing = true;
			}

			// maybe reveal next letter(s)
			float revealDurationCur = stillRevealing && submitKey.IsPressed() ? m_revealSecondsFast : m_revealSeconds;
			float nextRevealTime = m_lastRevealTime + revealDurationCur;
			if (stillRevealing && nextRevealTime <= Time.time)
			{
				// reveal next letter(s)
				int numToReveal = (int)((Time.time - nextRevealTime) / revealDurationCur) + 1;
				m_revealedCharCount = Math.Min(m_revealedCharCount + numToReveal, textCurLen);
				m_lastRevealTime += revealDurationCur * numToReveal;

				// update UI
				m_text.text = textCur?[0 .. m_revealedCharCount];

				if (m_queue.Count > 0 && m_revealedCharCount >= textCurLen)
				{
					yield return null; // to allow TMP to catch up with us & calculate bounds

					// display any replies
					bool active = m_queueFollowUp == null && lineCur.m_replies != null && lineCur.m_replies.Length > 0;
					m_replyTemplate.transform.parent.gameObject.SetActive(active);
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
								gameObject.SendMessage(replyCur.m_preconditionName, replyCur);
							}
							if (replyCur.m_deactivated)
							{
								continue;
							}

							GameObject newObj = Instantiate(m_replyTemplate, m_replyTemplate.transform.parent);
							newText = newObj.GetComponentInChildren<TMP_Text>();
							newText.text = replyCur.m_text;
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

						// set background size to fit
						RectTransform tf = m_replyTemplate.transform.parent.GetComponent<RectTransform>();
						tf.sizeDelta = new Vector2(tf.sizeDelta.x, Mathf.Abs(yOffsetCur) + yMargin * 2.0f);
					}
					else
					{
						// display continue indicator
						Extents lineExtents = m_text.textInfo.lineInfo[m_text.textInfo.lineCount - 1].lineExtents; // NOTE that lineInfo.Last() may be stale info
						m_continueIndicator.GetComponent<RectTransform>().anchoredPosition = new Vector2(lineExtents.max.x + m_indicatorSpacing, lineExtents.min.y + m_indicatorSpacing);
						m_continueIndicator.SetActive(true);
					}
				}
			}

			yield return null;

			if (m_loop && m_queue.Count <= 0)
			{
				m_queue = new(m_linesOrig);
			}
		}

		postDialogue?.Invoke();
		m_avatar.Controls.SwitchCurrentActionMap("Avatar"); // TODO: un-hardcode? check for other UI?
		gameObject.SetActive(false);
	}
}
