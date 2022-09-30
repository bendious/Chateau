using System.Linq;
using UnityEngine;


/// <summary>
/// Represents the current vital statistics of some game entity.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
	/// <summary>
	/// The maximum hit points for the entity.
	/// </summary>
	[SerializeField] private float m_maxHP = 1.0f;

	public AudioClip m_damageAudio;
	public AudioClip m_deathAudio;

	public const float m_invincibilityTimeDefault = 1.0f;
	public float m_invincibilityTime = m_invincibilityTimeDefault; // TODO: vary by animation played?
	public bool m_invincible;
	public Vector2 m_invincibilityDirection;

	public float m_minorDamageThreshold = 0.1f;

	[SerializeField] private bool m_gradientActive = false;
	[SerializeField] private Gradient m_gradient;

	[SerializeField] private float m_blinkSeconds = 0.3f;
	[SerializeField] private Color m_blinkColor = new(1.0f, 0.25f, 0.25f);
	[SerializeField] private float m_blinkSecondsPost = 0.25f;


	/// <summary>
	/// Indicates if the entity should be considered 'alive'.
	/// </summary>
	public bool IsAlive => m_currentHP > 0;

	public bool CanIncrement => m_currentHP < m_maxHP;

	public bool HealInProgress { get; private set; }

	public Color ColorCurrent => m_gradient.Evaluate(1.0f - m_currentHP / m_maxHP);


	protected float m_currentHP;


	private Animator m_animator;
	private KinematicCharacter m_character;


	/// <summary>
	/// Get the maximum HP of the entity.
	/// </summary>
	public float GetMax() => m_maxHP;

	/// <summary>
	/// Set the maximum HP of the entity, updating current HP as well.
	/// </summary>
	public virtual void SetMax(float hp)
	{
		float diff = m_maxHP - m_currentHP;
		m_maxHP = hp;

		if (m_currentHP <= 0.0f)
		{
			return; // "He's already dead!"
		}

		// update current health
		m_currentHP = m_maxHP - diff;
		if (m_currentHP <= 0.0f)
		{
			Decrement(null, 0.0f); // TODO: split out death function?
		}
	}

	/// <summary>
	/// Increment the HP of the entity.
	/// </summary>
	public virtual bool Increment(int amount = 1) => IncrementInternal(amount);

	/// <summary>
	/// Decrement the HP of the entity.
	/// </summary>
	public virtual bool Decrement(GameObject source, float amount = 1.0f)
	{
		if (m_invincible)
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

		// set item cause if appropriate, to enable chain reactions
		ItemController item = GetComponentInParent<ItemController>(); // TODO: ensure this doesn't catch unwanted ancestors?
		if (item != null && item.Cause == null)
		{
			item.SetCause(sourceCharacter);
		}

		// damage
		HealCancel();
		float amountFinal = -(sourceCharacter == null ? amount : sourceCharacter.m_damageScalar * amount);
		IncrementInternal(amountFinal);
		AudioSource audioSource = GetComponent<AudioSource>();
		if (m_damageAudio != null)
		{
			audioSource.PlayOneShot(m_damageAudio);
		}
		bool notMinor = amount >= m_minorDamageThreshold;
		if (m_animator != null && notMinor)
		{
			m_animator.SetTrigger("hurt");
		}
		OnHealthDecrement evt = Simulation.Schedule<OnHealthDecrement>();
		evt.m_health = this;
		evt.m_damageSource = source;
		evt.m_amountUnscaled = amount; // TODO: also give access to amountFinal?

		// death/despawn
		bool isDead = m_currentHP.FloatEqual(0.0f) && (!ConsoleCommands.NeverDie || GameController.Instance.m_avatars.All(avatar => avatar.gameObject != gameObject));
		if (isDead)
		{
			OnHealthDeath deathEvt = Simulation.Schedule<OnHealthDeath>();
			deathEvt.m_health = this;
			deathEvt.m_damageSource = source;

			// TODO: m_despawnOnDeath decoupled from m_character?
			if (m_character != null)
			{
				if (m_deathAudio != null)
				{
					audioSource.PlayOneShot(m_deathAudio);
				}
			}
			else
			{
				Simulation.Schedule<ObjectDespawn>(0.001f).m_object = gameObject; // NOTE the slight "delay" (though it will probably still fall in the same frame) to ensure OnHealth{Decrement/Death} are processed first since they may need to access the object
				if (m_deathAudio != null)
				{
					AudioSource.PlayClipAtPoint(m_deathAudio, transform.position); // NOTE that we can't use audioSource since we're despawning immediately // TODO: efficiency? hide & delay despawn until after audio?
				}
			}
		}

		// invincibility period
		if (!isDead && notMinor && m_invincibilityTime > 0.0f)
		{
			m_invincible = true;

			// TODO: disable from animation trigger & make timer a fallback?
			Simulation.Schedule<EnableDamage>(m_invincibilityTime).m_health = this;
			StartCoroutine(InvincibilityBlink(m_invincibilityTime - m_blinkSecondsPost));
		}

		return true;
	}

	public void HealStart(float delaySeconds, int amount, GameObject source)
	{
		Debug.Assert(!HealInProgress);
		HealInProgress = true;
		StartCoroutine(HealDelayed(delaySeconds, amount, source));
	}

	public void HealCancel() => HealInProgress = false; // NOTE that we don't forcibly stop HealDelayed() since it has cleanup to do

#if DEBUG
	/// <summary>
	/// Decrement the HP of the entity until HP reaches 0.
	/// </summary>
	public void Die()
	{
		Decrement(GameController.Instance.m_avatars.First().gameObject, m_currentHP); // NOTE that we can't use IncrementInternal() directly since that skips the death logic, and we can't use ourself or null as the source due to source checking logic // TODO: support using w/o any spawned avatars?
	}
#endif

	/// <summary>
	/// Increment the HP of the entity until HP reaches max.
	/// </summary>
	public virtual void Respawn()
	{
		IncrementInternal(m_maxHP - m_currentHP);
		m_invincible = false;
	}


	private void Awake()
	{
		m_animator = GetComponent<Animator>();
		m_character = GetComponent<KinematicCharacter>();
		m_currentHP = m_maxHP;
	}

	private void Start()
	{
		if (m_gradientActive) // TODO: don't assume that m_gradientActive is never set later?
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


	private bool IncrementInternal(float diff)
	{
		float hpPrev = m_currentHP;
		m_currentHP = Mathf.Clamp(m_currentHP + diff, 0.0f, m_maxHP);

		// NOTE that color is adjusted here rather than in SyncUI() to avoid stomping boss start color
		if (m_gradientActive)
		{
			Color color = ColorCurrent;
			foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>().Where(renderer => renderer.GetComponent<IAttachable>() == null))
			{
				renderer.color = color;
			}
		}

		return !Mathf.Approximately(m_currentHP, hpPrev);
	}

	private System.Collections.IEnumerator InvincibilityBlink(float secondsMax)
	{
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		if (renderer == null)
		{
			// TODO: support other types of renderer?
			yield break;
		}

		float timeMax = Time.time + secondsMax;
		float colorT = 0.0f;
		float tPerSec = 1.0f / m_blinkSeconds;
		Color colorOrig = renderer.color;

		while (m_invincible && Time.time < timeMax)
		{
			colorT = (colorT + Time.deltaTime * tPerSec).Modulo(1.0f); // NOTE that this is deliberately discontinuous when passing 1.0 // TODO: smoothly vary down as well as up?
			renderer.color = Color.Lerp(colorOrig, m_blinkColor, colorT);
			yield return null;
		}

		renderer.color = colorOrig;
	}

	private System.Collections.IEnumerator HealDelayed(float delaySeconds, int amount, GameObject source)
	{
		float speedPrev = m_character.maxSpeed;
		Debug.Assert(speedPrev > 0.0f); // TODO: better way of detecting/preventing multiple HealDelayed() instances in progress?
		m_character.maxSpeed = 0.0f; // TODO: also prevent jump/swing/etc?
		m_animator.SetBool("healing", true);

		// TODO: in-progress SFX/VFX, UI?

		float healTime = Time.time + delaySeconds;
		yield return new WaitUntil(() => !HealInProgress || Time.time >= healTime);

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
