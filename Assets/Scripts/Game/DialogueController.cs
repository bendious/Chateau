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
	[SerializeField] private TMP_Text m_replyTemplate;
	[SerializeField] private GameObject m_replyIndicator;

	public float m_revealSeconds = 0.1f;
	public float m_revealSecondsFast = 0.01f;

	public float m_indicatorSpacing = 7.5f;


	[Serializable] public class Line
	{
		[Serializable] public class Reply
		{
			public string m_text;
			public Line[] m_followUp;
			// TODO: editor-facing way to trigger specific functionality
		}

		public string m_text;
		public Reply[] m_replies;
	}


	public bool IsPlaying => gameObject.activeSelf;


	public void Play(Sprite sprite, Color spriteColor, Line[] textList, Action postDialogue, AvatarController avatar)
	{
		m_image.enabled = sprite != null;
		m_image.sprite = sprite;
		m_image.color = spriteColor;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		StartCoroutine(AdvanceDialogue(avatar, textList, postDialogue));
	}


	private IEnumerator AdvanceDialogue(AvatarController avatar, Line[] textList, Action postDialogue)
	{
		m_text.text = null;

		// avatar setup
		if (avatar == null)
		{
			yield return new WaitUntil(() => GameController.Instance.m_avatars.Count > 0);
			avatar = GameController.Instance.m_avatars.First(); // TODO: don't assume that the first avatar will always remain?
		}
		avatar.Controls.SwitchCurrentActionMap("UI"); // TODO: un-hardcode?
		InputAction submitKey = avatar.Controls.actions["Submit"];
		InputAction navigateKey = avatar.Controls.actions["Navigate"];

		// iterative info
		bool notDone = true;
		int textListIdx = -1;
		int revealedCharCount = 0;
		float lastRevealTime = Time.time;
		int textCurLen = 0;
		int replyIdx = 0;
		int followUpIdx = -1;

		while (notDone)
		{
			// handle input for reply selection
			if (m_replyIndicator.activeInHierarchy)
			{
				if (navigateKey.WasPressedThisFrame())
				{
					int dir = Mathf.RoundToInt(navigateKey.ReadValue<Vector2>().y);
					TMP_Text[] replies = m_replyTemplate.transform.parent.GetComponentsInChildren<TMP_Text>();
					replyIdx = (replyIdx + dir).Modulo(replies.Length);
					m_replyIndicator.GetComponent<RectTransform>().anchoredPosition3D = replies[replyIdx].GetComponent<RectTransform>().anchoredPosition + new Vector2(0.0f, m_indicatorSpacing);
				}
				else if (submitKey.WasPressedThisFrame())
				{
					// TODO: invoke any option-specific functionality

					if (followUpIdx == -1 && textList[textListIdx].m_replies != null && textList[textListIdx].m_replies[replyIdx].m_followUp != null && textList[textListIdx].m_replies[replyIdx].m_followUp.Length > 0)
					{
						followUpIdx = 0;
						revealedCharCount = 0;
						textCurLen = textList[textListIdx].m_replies[replyIdx].m_followUp.First().m_text.Length;
						lastRevealTime = Time.time;
					}
					m_replyIndicator.SetActive(false);
					m_replyTemplate.transform.parent.gameObject.SetActive(false);
					foreach (TMP_Text text in m_replyTemplate.transform.parent.GetComponentsInChildren<TMP_Text>())
					{
						if (text == m_replyTemplate)
						{
							continue;
						}
						Simulation.Schedule<ObjectDespawn>().m_object = text.gameObject;
					}
				}
			}

			// maybe move to next line
			bool stillRevealing = revealedCharCount < textCurLen;
			if (textListIdx < 0 || (submitKey.WasPressedThisFrame() && !stillRevealing))
			{
				// next line
				m_continueIndicator.SetActive(false);
				++textListIdx;
				followUpIdx = -1;
				notDone = textListIdx < textList.Length;
				revealedCharCount = 0;
				textCurLen = notDone ? textList[textListIdx].m_text.Length : 0;
				lastRevealTime = Time.time;
				stillRevealing = true;
			}

			// maybe reveal next letter(s)
			float revealDurationCur = stillRevealing && submitKey.IsPressed() ? m_revealSecondsFast : m_revealSeconds;
			float nextRevealTime = lastRevealTime + revealDurationCur;
			if (stillRevealing && nextRevealTime <= Time.time)
			{
				// reveal next letter(s)
				int numToReveal = (int)((Time.time - nextRevealTime) / revealDurationCur) + 1;
				revealedCharCount = Math.Min(revealedCharCount + numToReveal, textCurLen);
				lastRevealTime += revealDurationCur * numToReveal;

				// update UI
				Line lineCur = textList[textListIdx];
				m_text.text = notDone ? (followUpIdx >= 0 ? lineCur.m_replies[replyIdx].m_followUp[followUpIdx] : lineCur).m_text[0 .. revealedCharCount] : null;

				if (notDone && revealedCharCount >= textCurLen)
				{
					yield return null; // to allow TMP to catch up with us & calculate bounds

					// display any replies
					bool active = followUpIdx == -1 && textList[textListIdx].m_replies != null && textList[textListIdx].m_replies.Length > 0;
					m_replyTemplate.transform.parent.gameObject.SetActive(active);
					if (active)
					{
						TMP_Text newText = null;
						float yCur = 0.0f;
						for (int i = textList[textListIdx].m_replies.Length - 1; i >= 0; --i) // NOTE the bottom-to-top construction order since we anchor from the bottom
						{
							newText = Instantiate(m_replyTemplate, m_replyTemplate.transform.parent);
							newText.text = textList[textListIdx].m_replies[i].m_text;
							newText.GetComponent<RectTransform>().anchoredPosition += new Vector2(0.0f, yCur);
							newText.gameObject.SetActive(true);

							yield return null; // to allow TMP to catch up with us & calculate bounds
							yCur += newText.textBounds.size.y;
						}
						m_replyIndicator.GetComponent<RectTransform>().anchoredPosition = newText == null ? Vector2.zero : newText.GetComponent<RectTransform>().anchoredPosition + new Vector2(0.0f, m_indicatorSpacing);
						m_replyIndicator.SetActive(true);
						RectTransform tf = m_replyTemplate.transform.parent.GetComponent<RectTransform>();

						// set background size to fit
						tf.sizeDelta = new Vector2(tf.sizeDelta.x, yCur + newText.textBounds.extents.y);
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
		avatar.Controls.SwitchCurrentActionMap("Avatar"); // TODO: un-hardcode? check for other UI?
		gameObject.SetActive(false);
	}
}
