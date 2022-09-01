using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D), typeof(SpriteRenderer))]
public class InteractUpgrade : MonoBehaviour, IInteractable
{
	[SerializeField] private float m_activationTimeMin = 0.5f;

	[SerializeField] private WeightedObject<AudioClip>[] m_sfxActivate;

	[SerializeField] private string m_vfxGradientName = GameController.m_vfxGradientNameDefault;


	private AudioSource m_audio;
	private int m_vfxGradientID;

	private bool m_active;
	private float m_activationTime;


	private void Awake()
	{
		m_audio = GetComponentInChildren<AudioSource>(true);
		m_vfxGradientID = Shader.PropertyToID(m_vfxGradientName);
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && m_activationTime + m_activationTimeMin < Time.time && (m_active || GameController.Instance.UpgradeAvailable);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		// enable/disable upgrade
		// TODO: parameterize upgrade type
		System.Tuple<List<Color>, List<Gradient>> colors = GameController.Instance.HealthUpgrade(!m_active);

		ToggleActivation(colors);
	}


	public void ToggleActivation(System.Tuple<List<Color>, List<Gradient>> colors, bool silent = false)
	{
		m_active = !m_active;
		m_activationTime = Time.time;

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
		Debug.Assert(!m_active || (colors.Item1.Count == 0 && colors.Item2.Count == 0));

		// SFX
		if (m_active && !silent && m_sfxActivate.Length > 0)
		{
			m_audio.PlayOneShot(m_sfxActivate.RandomWeighted());
		}
	}
}
