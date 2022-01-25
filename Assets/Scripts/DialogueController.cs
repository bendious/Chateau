using System.Collections;
using TMPro;
using UnityEngine;


public class DialogueController : MonoBehaviour
{
	public TMP_Text m_text;
	public GameObject m_continueIndicator;

	public float m_revealSeconds = 0.1f;
	public float m_revealSecondsFast = 0.01f;

	public float m_indicatorSpacing = 10.0f;


	private string[] m_textList;
	private int m_textListIdx;
	private int m_revealedCharCount;


	public void Play(string[] textList)
	{
		m_textList = textList;
		m_textListIdx = -1;
		m_revealedCharCount = 0;

		gameObject.SetActive(true); // NOTE that this has to be BEFORE trying to start the coroutine
		StartCoroutine(AdvanceDialogue());
	}


	private IEnumerator AdvanceDialogue()
	{
		// iterative info
		bool notDone = true;
		float lastRevealTime = Time.time;
		int textCurLen = 0;

		while (notDone)
		{
			// maybe move to next line
			bool stillRevealing = m_revealedCharCount < textCurLen;
			if (m_textListIdx < 0 || (Input.GetKeyDown(KeyCode.Return) && !stillRevealing))
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
			float revealDurationCur = stillRevealing && Input.GetKey(KeyCode.Return) ? m_revealSecondsFast : m_revealSeconds;
			float nextRevealTime = lastRevealTime + revealDurationCur;
			if (stillRevealing && nextRevealTime <= Time.time)
			{
				// reveal next letter(s)
				int numToReveal = (int)((Time.time - nextRevealTime) / revealDurationCur) + 1;
				m_revealedCharCount = System.Math.Min(m_revealedCharCount + numToReveal, textCurLen);
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

		gameObject.SetActive(false);
	}
}