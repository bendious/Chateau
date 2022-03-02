using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


public class LightFlicker : MonoBehaviour
{
	[System.Serializable]
	public struct PeriodInfo
	{
		public float m_secondsMin;
		public float m_secondsMax;
		public float m_periodSecondsMin;
		public float m_periodSecondsMax;
	};


	public float m_intensityPctMin = 0.5f;

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


	private struct LightInfo
	{
		public Light2D m_light;
		public float m_intensityMax;
	}

	private struct RendererInfo
	{
		public SpriteRenderer m_renderer;
		public Color m_colorMax;
	}


	private LightInfo[] m_lights;
	private RendererInfo[] m_renderers;


	private float m_phase;

	private bool m_isFlickering;
	private float m_flickerToggleTime;


	private void OnEnable()
	{
		m_lights = GetComponentsInChildren<Light2D>(true).Select(light => new LightInfo { m_light = light, m_intensityMax = light.intensity }).ToArray();
		m_renderers = GetComponentsInChildren<SpriteRenderer>(true).Select(renderer => new RendererInfo { m_renderer = renderer, m_colorMax = renderer.color }).ToArray();

		m_phase = Random.Range(0.0f, 1.0f);
		m_flickerToggleTime = Time.time + Random.Range(m_nonFlickerInfo.m_secondsMin, m_nonFlickerInfo.m_secondsMax);

		StartCoroutine(UpdateIntensity());
	}

	private void OnDisable()
	{
		StopAllCoroutines();

		// ensure we don't lose intensity when disabled and enabled
		foreach (LightInfo info in m_lights)
		{
			info.m_light.intensity = info.m_intensityMax;
		}
		foreach (RendererInfo info in m_renderers)
		{
			info.m_renderer.color = info.m_colorMax;
		}
	}


	private IEnumerator UpdateIntensity()
	{
		PeriodInfo infoCur = m_isFlickering ? m_flickerInfo : m_nonFlickerInfo;

		while (true)
		{
			// start/end flickers
			if (m_flickerToggleTime <= Time.time)
			{
				m_isFlickering = !m_isFlickering;
				infoCur = m_isFlickering ? m_flickerInfo : m_nonFlickerInfo;
				m_flickerToggleTime = Time.time + Random.Range(infoCur.m_secondsMin, infoCur.m_secondsMax); // TODO: take health into account if destructible?

				// TODO: SFX?
			}

			// advance phase
			m_phase += Time.deltaTime / Random.Range(infoCur.m_periodSecondsMin, infoCur.m_periodSecondsMax); // TODO: take health into account if destructible?
			float intensityT = Mathf.Cos(m_phase * Mathf.PI * 2.0f) * 0.5f + 0.5f;

			// set intensity
			foreach (LightInfo info in m_lights)
			{
				info.m_light.intensity = Mathf.Lerp(m_intensityPctMin, info.m_intensityMax, intensityT);
			}
			foreach (RendererInfo info in m_renderers)
			{
				info.m_renderer.color = Color.Lerp(new Color(m_intensityPctMin, m_intensityPctMin, m_intensityPctMin) * info.m_colorMax, info.m_colorMax, intensityT);
			}

			yield return null; // TODO: non-constant updating for long-period lights?
		}
	}
}
