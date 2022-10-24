using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;


[DisallowMultipleComponent]
public class LightFlickerSynced : MonoBehaviour
{
	[SerializeField] private AnimationCurve m_intensityCurve = new() { keys = new Keyframe[] { new(0.0f, 0.5f, 0.0f, 0.0f), new(1.0f, 1.0f, 0.0f, 0.0f) }, preWrapMode = WrapMode.PingPong, postWrapMode = WrapMode.PingPong };

	[SerializeField] private WeightedObject<Sprite>[] m_sprites;

	[SerializeField] private WeightedObject<AudioClip>[] m_flickerSfx;
	[SerializeField] private float m_flickerSfxDelayMax = 0.0f;
	[SerializeField] private bool m_randomSfxStartPosition = false;


	public float IntensityScalar { private get; set; } = 1.0f;


	protected bool Synced { private get; set; } = true;
	protected bool IsFlickering { get; private set; }


	private struct ComponentInfo<TComp, TMax>
	{
		public TComp m_component;
		public TMax m_max;
	}


	private static int m_emissivePctID;


	private ComponentInfo<Light2D, float>[] m_lights;
	private ComponentInfo<SpriteRenderer, Color>[] m_renderers;
	private ComponentInfo<Graphic, Color>[] m_ui;


	protected virtual void OnEnable()
	{
		m_emissivePctID = Shader.PropertyToID("_EmissivePct"); // TODO: don't recalculate each time?

		m_lights = GetComponentsInChildren<Light2D>(true).Select(light => new ComponentInfo<Light2D, float> { m_component = light, m_max = light.intensity }).ToArray();
		m_renderers = GetComponentsInChildren<SpriteRenderer>(true).Select(renderer => new ComponentInfo<SpriteRenderer, Color> { m_component = renderer, m_max = renderer.color }).ToArray();
		m_ui = GetComponentsInChildren<Graphic>(true).Select(graphic => new ComponentInfo<Graphic, Color> { m_component = graphic, m_max = graphic.color }).ToArray();

		if (Synced)
		{
			GameController.Instance.m_lightFlickerMaster.AddChild(this);
		}
	}

	protected virtual void OnDisable()
	{
		// ensure we don't lose intensity when disabled and enabled
		foreach (ComponentInfo<Light2D, float> info in m_lights)
		{
			info.m_component.intensity = info.m_max;
		}
		foreach (ComponentInfo<SpriteRenderer, Color> info in m_renderers)
		{
			info.m_component.color = info.m_max;
		}
		foreach (ComponentInfo<Graphic, Color> info in m_ui)
		{
			info.m_component.color = info.m_max;
		}

		if (Synced)
		{
			GameController.Instance.m_lightFlickerMaster.RemoveChild(this);
		}
	}


	public void UpdateIntensity(bool isFlickering, float phase, bool randomizeSprite)
	{
		// start/end flickers
		if (isFlickering != IsFlickering)
		{
			IsFlickering = isFlickering;

			// SFX
			// TODO: support m_flickerSfxDelayMax and m_randomSFXStartPosition simultaneously?
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
						if (m_randomSfxStartPosition)
						{
							source.time = Random.Range(0.0f, source.clip.length);
						}
						source.Play();
					}
				}
			}

			SetSprites(randomizeSprite);
		}

		// set intensity
		// TODO: support for varying color as well?
		float intensityT = m_intensityCurve.Evaluate(phase) * IntensityScalar;
		Color colorPct = new(intensityT, intensityT, intensityT, 1.0f); // TODO: support flickering via alpha?
		foreach (ComponentInfo<Light2D, float> info in m_lights)
		{
			info.m_component.intensity = Mathf.Lerp(0.0f, info.m_max, intensityT);
		}
		foreach (ComponentInfo<SpriteRenderer, Color> info in m_renderers)
		{
			info.m_component.color = info.m_max * colorPct;
			foreach (Material material in info.m_component.materials)
			{
				material.SetFloat(m_emissivePctID, intensityT); // TODO: avoid creating separate material instances for each renderer w/o syncing un-synced LightFlicker components?
			}
		}
		foreach (ComponentInfo<Graphic, Color> info in m_ui)
		{
			info.m_component.color = info.m_max * colorPct;
		}
	}


	private void SetSprites(bool random)
	{
		if (m_sprites.Length <= 0 || (m_lights.Length <= 0 && m_renderers.Length <= 0))
		{
			return;
		}

		Sprite spriteNew = random ? m_sprites.RandomWeighted() : m_sprites.First().m_object;
		const bool flipX = false; // TODO: determine randomly if Light2D ever supports flipped light sprites
		foreach (ComponentInfo<Light2D, float> info in m_lights)
		{
			if (info.m_component.lightType != Light2D.LightType.Sprite)
			{
				continue;
			}

			info.m_component.NonpublicSetterWorkaround("m_LightCookieSprite", spriteNew);
		}
		foreach (ComponentInfo<SpriteRenderer, Color> info in m_renderers)
		{
			info.m_component.sprite = spriteNew;
			info.m_component.flipX = flipX;
		}
	}
}
