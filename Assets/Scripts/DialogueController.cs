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

	public float m_revealSeconds = 0.1f;
	public float m_revealSecondsFast = 0.01f;

	public float m_indicatorSpacing = 10.0f;


	public bool IsPlaying => gameObject.activeSelf;


	private string[] m_textList;
	private int m_textListIdx;
	private int m_revealedCharCount;

	private Action m_postDialogue;


	public void Play(Sprite sprite, Color spriteColor, string[] textList, Action postDialogue, AvatarController avatar)
	{
		m_image.enabled = sprite != null;
		m_image.sprite = sprite;
		m_image.color = spriteColor;
		m_textList = textList;
		m_textListIdx = -1;
		m_revealedCharCount = 0;
		m_postDialogue = postDialogue;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		StartCoroutine(AdvanceDialogue(avatar));
	}


	private IEnumerator AdvanceDialogue(AvatarController avatar)
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

		// iterative info
		bool notDone = true;
		float lastRevealTime = Time.time;
		int textCurLen = 0;

		while (notDone)
		{
			// maybe move to next line
			bool stillRevealing = m_revealedCharCount < textCurLen;
			if (m_textListIdx < 0 || (submitKey.WasPressedThisFrame() && !stillRevealing))
			{
				// next line
				m_continueIndicator.SetActive(false);
				++m_textListIdx;
				notDone = m_textListIdx < m_textList.Length;
				m_revealedCharCount = 0;
				textCurLen = notDone ? m_textList[m_textListIdx].Length : 0;
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
				m_revealedCharCount = Math.Min(m_revealedCharCount + numToReveal, textCurLen);
				lastRevealTime += revealDurationCur * numToReveal;

				// update UI
				m_text.text = notDone ? m_textList[m_textListIdx][0 .. m_revealedCharCount] : null;

				if (notDone && m_revealedCharCount >= textCurLen)
				{
					yield return null; // to allow TMP to catch up with us

					// display continue indicator
					Extents lineExtents = m_text.textInfo.lineInfo[m_text.textInfo.lineCount - 1].lineExtents; // NOTE that lineInfo.Last() may be stale info
					m_continueIndicator.GetComponent<RectTransform>().anchoredPosition = new Vector2(lineExtents.max.x + m_indicatorSpacing, lineExtents.min.y + m_indicatorSpacing);
					m_continueIndicator.SetActive(true);
				}
			}

			yield return null;
		}

		m_postDialogue?.Invoke();
		avatar.Controls.SwitchCurrentActionMap("Avatar"); // TODO: un-hardcode? check for other UI?
		gameObject.SetActive(false);
	}
}
