using System.Collections;
using System.Linq;
using UnityEngine;


public class MusicManager : MonoBehaviour
{
	[SerializeField] private WeightedObject<AudioClip>[] m_music; // TODO: order/weight by "intensity"?

	[SerializeField] private float m_delayMin = 60.0f;
	[SerializeField] private float m_delayMax = 240.0f;


	private AudioSource m_source;

	private float m_volumeOrig;
	private float m_nextPlayTime;

	private Coroutine m_fadeCoroutine;


	private void Awake()
	{
		m_source = GetComponent<AudioSource>();
		m_volumeOrig = m_source.volume;
	}

	private void OnEnable() => StartCoroutine(MusicSelectCoroutine());

	private void OnDisable()
	{
		StopAllCoroutines();
		m_fadeCoroutine = null;
	}


	public void Play(AudioClip clip)
	{
		StopFade(); // TODO: handle concurrent fade & play?

		m_source.clip = clip;
		m_source.volume = m_volumeOrig;
		m_source.Play();

		CalculateNextPlayTime();
	}

	public void FadeOut(float seconds)
	{
		StopFade();
		m_fadeCoroutine = StartCoroutine(FadeOutCoroutine(seconds));
	}

#if DEBUG
	public void DebugTest()
	{
		FadeOut(2.0f);
		m_nextPlayTime = Time.time;
	}
#endif


	private IEnumerator MusicSelectCoroutine()
	{
		CalculateNextPlayTime();
		WaitUntil waitCondition = new(() => m_nextPlayTime <= Time.time);

		while (true)
		{
			yield return waitCondition;

			if (m_source.isPlaying)
			{
				CalculateNextPlayTime();
			}
			else
			{
				float desiredWeight = GameController.Instance.ActiveEnemiesWeight();
				AudioClip clip = m_music.RandomWeighted(m_music.Select(weightedClip => 1.0f / (1.0f + Mathf.Abs(desiredWeight - weightedClip.m_weight)))).m_object;
				Play(clip);
			}
		}
	}

	private IEnumerator FadeOutCoroutine(float seconds)
	{
		float fadePerSec = m_source.volume / seconds;
		while (m_source.volume > 0.0f)
		{
			m_source.volume -= fadePerSec * Time.deltaTime;
			yield return null;
		}
		m_source.Stop();
	}

	private void StopFade()
	{
		if (m_fadeCoroutine != null)
		{
			StopCoroutine(m_fadeCoroutine);
			m_fadeCoroutine = null;
		}
	}

	private void CalculateNextPlayTime() => m_nextPlayTime = Time.time + (m_source.clip == null ? 0 : m_source.clip.length - m_source.time) + Random.Range(m_delayMin, m_delayMax);
}
