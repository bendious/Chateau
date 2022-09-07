using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;


[DisallowMultipleComponent]
public class InteractUpgrade : MonoBehaviour, IInteractable
{
	private enum Type
	{
		None,
		Health,
		Lighting,
		Damage,
	}


	public int m_index = -1;

	public InteractUpgrade[] m_sources;

	[SerializeField] private Type m_type;

	[SerializeField] private float m_activationTimeMin = 0.5f;

	[SerializeField] private WeightedObject<AudioClip>[] m_sfxActivate;

	[SerializeField] private string m_vfxGradientName = "LifetimeColor";


	private AudioSource m_audio;
	private int m_vfxGradientID;

	private bool m_active;
	private float m_activationTime;


	private void Awake()
	{
		m_audio = GetComponentInChildren<AudioSource>(true);
		m_vfxGradientID = Shader.PropertyToID(m_vfxGradientName);
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && m_index >= 0 && m_type != Type.None && m_activationTime + m_activationTimeMin < Time.time && (m_active || m_sources.Any(source => source.m_active));

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		// enable/disable upgrade
		switch (m_type)
		{
			// NOTE that we purposely don't handle Type.None since CanInteract() should stop us from ever getting here
			case Type.Health: GameController.Instance.HealthUpgrade(!m_active, m_index); break;
			case Type.Lighting: GameController.Instance.LightingUpgrade(!m_active, m_index); break;
			case Type.Damage: GameController.Instance.DamageUpgrade(!m_active, m_index); break;
			default: Debug.LogError("Unhandled InteractUpgrade.Type"); break;
		}

		ToggleActivation();
	}


	public void ToggleActivation(bool silent = false)
	{
		m_active = !m_active;
		m_activationTime = Time.time;

		InteractUpgrade colorSource = m_sources.Length <= 0 ? this : m_sources.First(source => source.m_active == m_active);
		if (colorSource != this)
		{
			colorSource.ToggleActivation(silent);
		}
		Tuple<List<Color>, List<Gradient>> colors = m_active ? colorSource.FindColors() : null;

		for (int i = 0; i < transform.childCount; ++i)
		{
			// activate children
			GameObject childObj = transform.GetChild(i).gameObject;
			childObj.SetActive(true);

			// enable/disable VFX
			foreach (VisualEffect vfx in childObj.GetComponentsInChildren<VisualEffect>(true))
			{
				if (m_active)
				{
					// match color(s)
					Light2D light = vfx.GetComponent<Light2D>();
					if (light != null)
					{
						light.color = colors.Item1.First();
						colors.Item1.RemoveAt(0);
					}
					vfx.SetGradient(m_vfxGradientID, colors.Item2.First());
					colors.Item2.RemoveAt(0);

					vfx.Play();
				}
				else
				{
					StartCoroutine(vfx.SoftStop(() => m_active));
				}
			}
		}
		Debug.Assert(colors == null || (colors.Item1.Count == 0 && colors.Item2.Count == 0));

		// SFX
		if (m_active && !silent && m_sfxActivate.Length > 0)
		{
			m_audio.PlayOneShot(m_sfxActivate.RandomWeighted());
		}
	}

	private Tuple<List<Color>, List<Gradient>> FindColors()
	{
		Tuple<List<Color>, List<Gradient>> colors = Tuple.Create(new List<Color>(), new List<Gradient>());

		foreach (VisualEffect vfx in GetComponentsInChildren<VisualEffect>(true))
		{
			Light2D light = vfx.GetComponent<Light2D>();
			if (light != null)
			{
				colors.Item1.Add(light.color);
			}
			colors.Item2.Add(vfx.GetGradient(m_vfxGradientName));
		}

		return colors;
	}
}
