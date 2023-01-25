using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


/// <summary>
/// Represents the current vital statistics of some game entity.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
	[Serializable] public enum DamageType
	{
		Generic = -1,
		Blunt,
		Edge,
		Pierce,
		Heat,
		Electric,
		Chemical,
		Shatter,
	}


	/// <summary>
	/// The maximum hit points for the entity.
	/// </summary>
	[SerializeField] private float m_maxHP = 1.0f;

	[SerializeField] private float m_currentHP;
	public float CurrentHP => m_currentHP;

	public WeightedObject<AudioClip>[] m_damageSFX;
	public WeightedObject<AudioClip>[] m_deathSFX;

	public float m_invincibilityTime = 0.0f; // TODO: vary by animation played?
	public bool m_invincible;
	public Vector2 m_invincibilityDirection;

	[SerializeField] private float m_damageMergeSeconds = 0.25f;

	public float m_minorDamageThreshold = 0.1f;

	public bool m_gradientActive = false;
	[SerializeField] private Gradient m_gradient;

	[SerializeField] private float m_blinkSeconds = 0.3f;
	[SerializeField] private Color m_blinkColor = new(1.0f, 0.25f, 0.25f);
	[SerializeField] private float m_blinkSecondsPost = 0.25f;
	[SerializeField] private float m_blinkLightRadiusMax = 5.0f;

	[Serializable] private struct DamageTypeScalar {
		public DamageType m_type;
		public float m_scalar;
	}
	[SerializeField] private DamageTypeScalar[] m_typeScalars;


	/// <summary>
	/// Indicates if the entity should be considered 'alive'.
	/// </summary>
	public bool IsAlive => CurrentHP > 0;

	public bool CanIncrement => CurrentHP < m_maxHP;

	public bool HealInProgress { get; private set; }

	public float PercentHP => CurrentHP / m_maxHP;
	public Color ColorCurrent => m_gradient.Evaluate(1.0f - PercentHP);


	private Animator m_animator;
	private KinematicCharacter m_character;

	private float m_lastDamageTime; // NOTE that this excludes minor damage
	private float m_lastDamageAmount; // NOTE that this is pre-scaled damage amount and excludes minor damage
	private DamageType m_lastDamageType;


	/// <summary>
	/// Get the maximum HP of the entity.
	/// </summary>
	public float GetMax() => m_maxHP;

	/// <summary>
	/// Set the maximum HP of the entity, updating current HP as well.
	/// </summary>
	public virtual void SetMax(float hp)
	{
		float diff = m_maxHP - CurrentHP;
		m_maxHP = hp;

		if (CurrentHP <= 0.0f)
		{
			return; // "He's already dead!"
		}

		// update current health
		SetCurrent(m_maxHP - diff);
		if (CurrentHP <= 0.0f)
		{
			Decrement(null, null, 0.0f, DamageType.Generic); // TODO: split out death function?
		}
	}

	/// <summary>
	/// Set the current HP of the entity. Skips all player-facing effects such as animation/SFX/blink/etc.
	/// </summary>
	public virtual void SetCurrent(float hp) => m_currentHP = Mathf.Clamp(hp, 0.0f, m_maxHP);

	/// <summary>
	/// Increment the HP of the entity.
	/// </summary>
	public virtual bool Increment(int amount = 1) => IncrementInternal(amount);

	/// <summary>
	/// Decrement the HP of the entity.
	/// </summary>
	public virtual bool Decrement(GameObject source, GameObject directCause, float amount, DamageType type) // TODO: types[]?
	{
		bool notMinor = amount >= m_minorDamageThreshold;
		bool mergeWithPrevious = notMinor && type == m_lastDamageType && m_lastDamageTime + m_damageMergeSeconds >= Time.time;
		if (!mergeWithPrevious && m_invincible)
		{
			return false;
		}

		// check whether object should be immune to damage from source object
		KinematicCharacter sourceCharacter = source == null ? null : source.GetComponent<KinematicCharacter>();
		if (sourceCharacter != null && !sourceCharacter.CanDamage(gameObject))
		{
			return false;
		}

		// check directional invincibility
		if (source != null && Vector2.Dot(m_invincibilityDirection, source.transform.position - transform.position) > 0.0f) // TODO: don't assume the source is in the same direction as the weapon?
		{
			return false;
		}

		// NOTE that we merge BEFORE scaling to preserve the final summed amount
		if (mergeWithPrevious)
		{
			if (amount <= m_lastDamageAmount)
			{
				return false;
			}
			amount -= m_lastDamageAmount;
			m_lastDamageAmount += amount;
		}
		else if (notMinor)
		{
			m_lastDamageTime = Time.time;
			m_lastDamageAmount = amount;
			m_lastDamageType = type;
		}

		// scale
		float amountFinal = -(sourceCharacter == null ? amount : sourceCharacter.m_damageScalar * amount);
		foreach (DamageTypeScalar typeScalar in m_typeScalars)
		{
			if (typeScalar.m_type == type)
			{
				amountFinal *= typeScalar.m_scalar;
			}
		}
		if (amountFinal.FloatEqual(0.0f))
		{
			return false;
		}

		// set item/effect cause if appropriate, to enable chain reactions
		ItemController item = GetComponentInParent<ItemController>(); // TODO: ensure this doesn't catch unwanted ancestors?
		if (item != null && item.Cause == null)
		{
			item.SetCause(sourceCharacter);
		}
		if (TryGetComponent(out DespawnEffect effect))
		{
			effect.CauseExternal = sourceCharacter;
		}

		// damage
		HealCancel();
		IncrementInternal(amountFinal);
		OnHealthDecrement evt = Simulation.Schedule<OnHealthDecrement>();
		evt.m_health = this;
		evt.m_damageSource = source;
		evt.m_directCause = directCause;
		evt.m_amountUnscaled = amount; // TODO: also give access to amountFinal?

		// effects
		AudioSource audioSource = GetComponent<AudioSource>();
		if (!mergeWithPrevious)
		{
			if (m_damageSFX.Length > 0)
			{
				audioSource.PlayOneShot(m_damageSFX.RandomWeighted());
			}
			if (m_animator != null && notMinor)
			{
				m_animator.SetTrigger("hurt");
			}
		}

		// death/despawn
		bool isDead = CurrentHP.FloatEqual(0.0f) && (!ConsoleCommands.NeverDie || GameController.Instance.m_avatars.All(avatar => avatar.gameObject != gameObject));
		if (isDead)
		{
			OnHealthDeath deathEvt = Simulation.Schedule<OnHealthDeath>();
			deathEvt.m_health = this;
			deathEvt.m_damageSource = source;

			// TODO: m_despawnOnDeath decoupled from m_character?
			if (m_character != null)
			{
				if (m_deathSFX.Length > 0)
				{
					audioSource.PlayOneShot(m_deathSFX.RandomWeighted());
				}
			}
			else
			{
				Simulation.Schedule<ObjectDespawn>(0.001f).m_object = gameObject; // NOTE the slight "delay" (though it will probably still fall in the same frame) to ensure OnHealth{Decrement/Death} are processed first since they may need to access the object
				if (m_deathSFX.Length > 0)
				{
					AudioSource.PlayClipAtPoint(m_deathSFX.RandomWeighted(), transform.position); // NOTE that we can't use audioSource since we're despawning immediately // TODO: efficiency? hide & delay despawn until after audio?
				}
			}
		}

		// invincibility period
		if (!isDead && notMinor && !mergeWithPrevious && m_invincibilityTime > 0.0f)
		{
			m_invincible = true;

			// TODO: disable from animation trigger & make timer a fallback?
			Simulation.Schedule<EnableDamage>(m_invincibilityTime).m_health = this;
			StartCoroutine(InvincibilityBlink(m_invincibilityTime - m_blinkSecondsPost));
		}

		return true;
	}

	public bool HealStart(float delaySeconds, int amount, GameObject source)
	{
		if (!CanIncrement || HealInProgress)
		{
			return false;
		}
		HealInProgress = true;
		StartCoroutine(HealDelayed(delaySeconds, amount, source));
		return true;
	}

	public void HealCancel() => HealInProgress = false; // NOTE that we don't forcibly stop HealDelayed() since it has cleanup to do

	/// <summary>
	/// Increment the HP of the entity until HP reaches max.
	/// </summary>
	public virtual void Respawn()
	{
		IncrementInternal(m_maxHP - CurrentHP);
		m_invincible = false;
	}


	private void Awake()
	{
		m_animator = GetComponent<Animator>();
		m_character = GetComponent<KinematicCharacter>();
		if (CurrentHP <= 0.0f)
		{
			SetCurrent(m_maxHP);
		}
	}

	private void Start()
	{
		if (m_gradientActive) // NOTE that this may be set later, but that means we don't want to color-match initially
		{
			SpriteRenderer r = GetComponent<SpriteRenderer>();
			GradientColorKey[] colorKeys = m_gradient.colorKeys;
			if (r != null && r.color != colorKeys.First().color)
			{
				colorKeys[0].color = r.color;
				m_gradient.colorKeys = colorKeys; // NOTE that we have to replace the whole array since trying to modify m_gradient.colorKeys[0] results in modifying only a local copy...
			}
		}
	}


	private void Recolor(Color color, float lightRadiusMax) // TODO: support separate colors for sprites/lights?
	{
		static bool isColorable(Component comp) => comp.GetComponentInParent<IAttachable>() == null && comp.GetComponent<ArmController>() == null; // we don't want to recolor attached items, and ArmController takes care of mirroring color itself // TODO: also ensure the component does not have a separate rigid body or ColorRandomizer?

		// TODO: efficiency?
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>().Where(isColorable))
		{
			renderer.color = color;
		}

		foreach (Light2D light in GetComponentsInChildren<Light2D>().Where(isColorable))
		{
			if (light.pointLightOuterRadius > lightRadiusMax)
			{
				continue;
			}
			light.color = color;
		}
	}

	private bool IncrementInternal(float diff)
	{
		float hpPrev = CurrentHP;
		SetCurrent(CurrentHP + diff);

		if (m_gradientActive)
		{
			Recolor(ColorCurrent, float.MaxValue);
		}

		return !Mathf.Approximately(CurrentHP, hpPrev);
	}

	private IEnumerator InvincibilityBlink(float secondsMax)
	{
		SpriteRenderer firstSprite = GetComponentInChildren<SpriteRenderer>();
		if (firstSprite == null && GetComponentInChildren<Light2D>() == null) // TODO: better early-out conditions?
		{
			// TODO: support other types of renderer?
			yield break;
		}

		float timeMax = Time.time + secondsMax;
		float colorT = 0.0f;
		float tPerSec = 1.0f / m_blinkSeconds;
		Color colorOrig = firstSprite.color;

		while (m_invincible && Time.time < timeMax)
		{
			colorT = (colorT + Time.deltaTime * tPerSec).Modulo(1.0f); // NOTE that this is deliberately discontinuous when passing 1.0 // TODO: smoothly vary down as well as up?
			Recolor(Color.Lerp(colorOrig, m_blinkColor, colorT), m_blinkLightRadiusMax);
			yield return null;
		}

		Recolor(colorOrig, m_blinkLightRadiusMax);
	}

	private IEnumerator HealDelayed(float delaySeconds, int amount, GameObject source)
	{
		float speedPrev = m_character.maxSpeed;
		Debug.Assert(speedPrev > 0.0f); // multiple HealDelayed() instances at once, not prevented by HealStart()? // TODO: don't assume all healable characters have non-zero speed?
		m_character.maxSpeed = 0.0f; // NOTE that this also prevents jump but still allows swinging
		m_animator.SetBool("healing", true);

		// TODO: in-progress SFX/VFX?

		// UI
		// TODO: move into HealthUI?
		AvatarController avatar = m_character as AvatarController;
		UnityEngine.UI.Image progressMeter = avatar == null ? null : avatar.m_progressMeterUI;
		Vector3 uiOffset = new(0.0f, m_character.GetComponent<Collider2D>().bounds.extents.y);

		// per-frame update / wait
		float healTime = Time.time + delaySeconds;
		bool isDone() => !HealInProgress || Time.time >= healTime;
		if (progressMeter != null)
		{
			progressMeter.gameObject.SetActive(true);
			while (!isDone())
			{
				progressMeter.transform.position = Camera.main.WorldToScreenPoint(m_character.transform.position + uiOffset); // TODO: worldspace canvas?
				progressMeter.fillAmount = 1.0f - (healTime - Time.time) / delaySeconds;
				yield return null;
			}
			progressMeter.gameObject.SetActive(false);
		}
		else
		{
			yield return new WaitUntil(isDone);
		}

		m_animator.SetBool("healing", false);
		m_character.maxSpeed = speedPrev;

		if (!HealInProgress)
		{
			yield break;
		}

		bool healed = Increment(amount);
		if (healed)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = source;

			// TODO: success SFX/VFX
		}
	}
}
