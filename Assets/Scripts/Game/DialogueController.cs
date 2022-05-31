using System;
using System.Collections;
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
			public string[] m_followUp;
			public string m_eventName; // TODO: less error-prone type?
		}

		public string m_text;
		public Reply[] m_replies;
	}


	public bool IsPlaying => gameObject.activeSelf;


	private Line[] m_textList;
	private int m_textListIdx;
	private int m_revealedCharCount;
	private float m_lastRevealTime;
	private int m_replyIdx;
	private int m_followUpIdx;
	private int m_textCurLen;
	private AvatarController m_avatar;

	private bool m_replySelected = false;


	public void Play(Sprite sprite, Color spriteColor, Line[] textList, Action postDialogue, AvatarController avatar)
	{
		m_image.enabled = sprite != null;
		m_image.sprite = sprite;
		m_image.color = spriteColor;

		m_textList = textList;
		m_textListIdx = -1;
		m_revealedCharCount = 0;
		m_lastRevealTime = Time.time;
		m_replyIdx = 0;
		m_followUpIdx = -1;
		m_textCurLen = 0;
		m_avatar = avatar;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		StartCoroutine(AdvanceDialogue(postDialogue));
	}

	public void OnReplySelected(GameObject replyObject)
	{
		m_replyIdx = replyObject.transform.GetSiblingIndex() - 1; // -1 due to deactivated template object

		if (!string.IsNullOrEmpty(m_textList[m_textListIdx].m_replies[m_replyIdx].m_eventName))
		{
			gameObject.SendMessage(m_textList[m_textListIdx].m_replies[m_replyIdx].m_eventName);
		}

		if (m_followUpIdx == -1 && m_textList[m_textListIdx].m_replies != null && m_textList[m_textListIdx].m_replies[m_replyIdx].m_followUp != null && m_textList[m_textListIdx].m_replies[m_replyIdx].m_followUp.Length > 0)
		{
			m_followUpIdx = 0;
			m_revealedCharCount = 0;
			m_textCurLen = m_textList[m_textListIdx].m_replies[m_replyIdx].m_followUp.First().Length;
			m_lastRevealTime = Time.time;
		}
		else
		{
			m_replySelected = true;
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

	public void MerchantSell()
	{
		IAttachable[] attachables = m_avatar.GetComponentsInChildren<IAttachable>();
		if (attachables.Length <= 0)
		{
			m_textList[m_textListIdx].m_replies[m_replyIdx].m_followUp = new string[] { "No you don't..." };
		}
		else
		{
			m_textList[m_textListIdx].m_replies[m_replyIdx].m_followUp = new string[] { "Excellent!" };
		}
	}


	private IEnumerator AdvanceDialogue(Action postDialogue)
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

		bool notDone = true;
		while (notDone)
		{
			// handle input for reply selection
			if (m_replyTemplate.transform.parent.gameObject.activeInHierarchy)
			{
				// wait for OnReplySelected()
				WaitUntil waitCondition = new(() => !m_replyTemplate.transform.parent.gameObject.activeInHierarchy);
				yield return waitCondition;
				continue;
			}

			// maybe move to next line
			bool stillRevealing = m_revealedCharCount < m_textCurLen;
			if (m_textListIdx < 0 || m_replySelected || (submitKey.WasPressedThisFrame() && !stillRevealing))
			{
				// next line
				m_continueIndicator.SetActive(false);
				++m_textListIdx;
				m_followUpIdx = -1;
				notDone = m_textListIdx < m_textList.Length;
				m_revealedCharCount = 0;
				m_textCurLen = notDone ? m_textList[m_textListIdx].m_text.Length : 0;
				m_lastRevealTime = Time.time;
				m_replySelected = false;
				stillRevealing = true;
			}

			// maybe reveal next letter(s)
			float revealDurationCur = stillRevealing && submitKey.IsPressed() ? m_revealSecondsFast : m_revealSeconds;
			float nextRevealTime = m_lastRevealTime + revealDurationCur;
			if (stillRevealing && nextRevealTime <= Time.time)
			{
				// reveal next letter(s)
				int numToReveal = (int)((Time.time - nextRevealTime) / revealDurationCur) + 1;
				m_revealedCharCount = Math.Min(m_revealedCharCount + numToReveal, m_textCurLen);
				m_lastRevealTime += revealDurationCur * numToReveal;

				// update UI
				Line lineCur = m_textList[m_textListIdx];
				m_text.text = notDone ? (m_followUpIdx >= 0 ? lineCur.m_replies[m_replyIdx].m_followUp[m_followUpIdx] : lineCur.m_text)[0 .. m_revealedCharCount] : null;

				if (notDone && m_revealedCharCount >= m_textCurLen)
				{
					yield return null; // to allow TMP to catch up with us & calculate bounds

					// display any replies
					bool active = m_followUpIdx == -1 && m_textList[m_textListIdx].m_replies != null && m_textList[m_textListIdx].m_replies.Length > 0;
					m_replyTemplate.transform.parent.gameObject.SetActive(active);
					if (active)
					{
						TMP_Text newText = null;
						float yMargin = 0.0f;
						float yOffsetCur = 0.0f;
						for (int i = 0, n = m_textList[m_textListIdx].m_replies.Length; i < n; ++i)
						{
							GameObject newObj = Instantiate(m_replyTemplate, m_replyTemplate.transform.parent);
							newText = newObj.GetComponentInChildren<TMP_Text>();
							newText.text = m_textList[m_textListIdx].m_replies[i].m_text;
							RectTransform newTf = newObj.GetComponent<RectTransform>();
							if (i == 0)
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
		}

		postDialogue?.Invoke();
		m_avatar.Controls.SwitchCurrentActionMap("Avatar"); // TODO: un-hardcode? check for other UI?
		gameObject.SetActive(false);
	}
}
