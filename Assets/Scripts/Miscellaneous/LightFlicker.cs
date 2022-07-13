using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent]
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


	public AnimationCurve m_intensityCurve = new() { keys = new Keyframe[] { new(0.0f, 0.5f, 0.0f, 0.0f), new(1.0f, 1.0f, 0.0f, 0.0f) }, preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.PingPong };
	public bool m_fullCurveFlicker = false;
	[SerializeField] private bool m_randomSFXStartPosition = false;

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

	[SerializeField] private WeightedObject<Sprite>[] m_sprites;

	public WeightedObject<AudioClip>[] m_flickerSfx;
	public float m_flickerSfxDelayMax = 0.0f;


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


	private static int m_emissivePctID;


	private LightInfo[] m_lights;
	private RendererInfo[] m_renderers;


	private float m_phase;

	private bool m_isFlickering;
	private float m_flickerToggleTime;
	private float m_flickerLength;


	private void OnEnable()
	{
		m_emissivePctID = Shader.PropertyToID("_EmissivePct");

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


	private void SetSprites(bool random)
	{
		if (m_sprites.Length <= 0 || m_lights.Length <= 0)
		{
			return;
		}

		Sprite spriteNew = random ? m_sprites.RandomWeighted() : m_sprites.First().m_object; // TODO: parameterize non-flicker sprite behavior?
		foreach (LightInfo info in m_lights)
		{
			if (info.m_light.lightType != Light2D.LightType.Sprite)
			{
				continue;
			}

			info.m_light.NonpublicSetterWorkaround("m_LightCookieSprite", spriteNew);
		}
		foreach (RendererInfo info in m_renderers)
		{
			info.m_renderer.sprite = spriteNew;
		}
	}

	private IEnumerator UpdateIntensity()
	{
		PeriodInfo infoCur = m_isFlickering ? m_flickerInfo : m_nonFlickerInfo;

		yield return null; // to prevent initial values being stomped on for some reason

		while (true)
		{
			// start/end flickers
			// TODO: allow syncronization across instances
			if (m_flickerToggleTime <= Time.time)
			{
				m_isFlickering = !m_isFlickering;
				infoCur = m_isFlickering ? m_flickerInfo : m_nonFlickerInfo;
				m_flickerLength = Random.Range(infoCur.m_secondsMin, infoCur.m_secondsMax); // TODO: take health into account if destructible?
				m_flickerToggleTime = Time.time + m_flickerLength;

				// SFX
				if (m_flickerSfx.Length > 0)
				{
					AudioSource source = GetComponentInParent<AudioSource>();
					if (!source.isPlaying)
					{
						source.clip = m_flickerSfx.RandomWeighted();
						if (m_flickerSfxDelayMax > 0.0f)
						{
							source.PlayScheduled(AudioSettings.dspTime + Random.Range(0.0f, m_flickerSfxDelayMax)); // TODO: prevent getting pre-empted by something else playing on this source?
						}
						else
						{
							source.Stop();
							if (m_randomSFXStartPosition)
							{
								source.time = Random.Range(0.0f, source.clip.length);
							}
							source.Play();
						}
					}
				}

				SetSprites(m_isFlickering);
			}

			// maybe advance phase
			if (m_fullCurveFlicker && !m_isFlickering)
			{
				m_phase = 0.0f;
			}
			else
			{
				m_phase += Time.deltaTime / (m_fullCurveFlicker && m_isFlickering ? m_flickerLength : Random.Range(infoCur.m_periodSecondsMin, infoCur.m_periodSecondsMax)); // TODO: take health into account if destructible?
			}

			// set intensity
			float intensityT = m_intensityCurve.Evaluate(m_phase);
			foreach (LightInfo info in m_lights)
			{
				info.m_light.intensity = Mathf.Lerp(0.0f, info.m_intensityMax, intensityT);
			}
			foreach (RendererInfo info in m_renderers)
			{
				info.m_renderer.color = info.m_colorMax * new Color(intensityT, intensityT, intensityT, 1.0f);
				foreach (Material material in info.m_renderer.materials)
				{
					material.SetFloat(m_emissivePctID, intensityT); // TODO: avoid creating separate material instances for each renderer?
				}
			}

			yield return m_fullCurveFlicker && !m_isFlickering ? new WaitForSeconds(m_flickerToggleTime - Time.time) : null; // TODO: non-constant updating also for long-period lights?
		}
	}
}
