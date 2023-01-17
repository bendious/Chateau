using System.Collections;
using UnityEngine;


[DisallowMultipleComponent]
public sealed class LightFlicker : LightFlickerSynced
{
	[System.Serializable]
	public struct PeriodInfo
	{
		public float m_secondsMin;
		public float m_secondsMax;
		public float m_periodSecondsMin;
		public float m_periodSecondsMax;
	};


	[SerializeField] private bool m_fullCurveFlicker = false;

	public PeriodInfo m_nonFlickerInfo = new()
	{
		m_secondsMin = 1.0f,
		m_secondsMax = 10.0f,
		m_periodSecondsMin = 4.0f,
		m_periodSecondsMax = 8.0f,
	};
	public PeriodInfo m_flickerInfo = new()
	{
		m_secondsMin = 0.1f,
		m_secondsMax = 0.5f,
		m_periodSecondsMin = 0.1f,
		m_periodSecondsMax = 0.5f,
	};


	private System.Collections.Generic.List<LightFlickerSynced> m_children;

	private float m_phase;

	private float m_flickerToggleTime;
	private float m_flickerLength;


	protected override void OnEnable()
	{
		Synced = false;
		base.OnEnable();

		m_phase = Random.Range(0.0f, 1.0f);
		m_flickerToggleTime = Time.time + Random.Range(m_nonFlickerInfo.m_secondsMin, m_nonFlickerInfo.m_secondsMax);

		StartCoroutine(UpdateIntensityCoroutine());
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		StopAllCoroutines();
	}


	public void AddChild(LightFlickerSynced child)
	{
		m_children ??= new();
		child.UpdateIntensity(false, m_phase, false);
		m_children.Add(child);
	}

	public void RemoveChild(LightFlickerSynced child) => m_children.Remove(child);


	private IEnumerator UpdateIntensityCoroutine()
	{
		PeriodInfo infoCur = IsFlickering ? m_flickerInfo : m_nonFlickerInfo;

		yield return null; // to prevent initial values being stomped on for some reason

		while (true)
		{
			// start/end flickers
			bool toggleFlicker = m_flickerToggleTime <= Time.time;
			bool isFlickeringNew = toggleFlicker ? !IsFlickering : IsFlickering;
			if (toggleFlicker)
			{
				infoCur = isFlickeringNew ? m_flickerInfo : m_nonFlickerInfo;
				m_flickerLength = Random.Range(infoCur.m_secondsMin, infoCur.m_secondsMax); // TODO: take health into account if destructible?
				m_flickerToggleTime = Time.time + m_flickerLength;
			}

			// maybe advance phase
			if (m_fullCurveFlicker && !isFlickeringNew)
			{
				m_phase = 0.0f;
			}
			else
			{
				m_phase += Time.deltaTime / (m_fullCurveFlicker && isFlickeringNew ? m_flickerLength : Random.Range(infoCur.m_periodSecondsMin, infoCur.m_periodSecondsMax)); // TODO: take health into account if destructible?
			}

			// update visuals/audio
			bool togglingOn = toggleFlicker && isFlickeringNew;
			UpdateIntensity(isFlickeringNew, m_phase, togglingOn);

			// advance children
			if (m_children != null)
			{
				LightFlickerSynced randomizedSpriteChild = togglingOn ? m_children.Random() : null;
				foreach (LightFlickerSynced child in m_children)
				{
					child.UpdateIntensity(isFlickeringNew, m_phase, child == randomizedSpriteChild);
				}
			}

			yield return m_fullCurveFlicker && !isFlickeringNew ? new WaitForSeconds(m_flickerToggleTime - Time.time) : null; // TODO: non-constant updating also for long-period lights?
		}
	}
}
