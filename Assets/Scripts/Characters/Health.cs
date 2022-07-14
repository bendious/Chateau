using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;


/// <summary>
/// Represents the current vital statistics of some game entity.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
	/// <summary>
	/// The maximum hit points for the entity.
	/// </summary>
	public float m_maxHP = 1.0f;

	public GameObject m_healthUIParent;
	public Sprite m_healthSprite;
	public Sprite m_healthMissingSprite;
	public float m_UIPadding = 5.0f;

	public AudioClip m_damageAudio;
	public AudioClip m_deathAudio;

	public const float m_invincibilityTimeDefault = 1.0f;
	public float m_invincibilityTime = m_invincibilityTimeDefault; // TODO: vary by animation played?
	public bool m_invincible;

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


	float m_currentHP;

	private Animator m_animator;
	private KinematicCharacter m_character;


	/// <summary>
	/// Increment the HP of the entity.
	/// </summary>
	public bool Increment(int amount = 1)
	{
		bool changed = IncrementInternal(amount);
		SyncUI();
		return changed;
	}

	/// <summary>
	/// Decrement the HP of the entity.
	/// </summary>
	public bool Decrement(GameObject source, float amount = 1.0f)
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

		// damage
		HealCancel();
		IncrementInternal(-1.0f * amount);
		AudioSource audioSource = GetComponent<AudioSource>();
		if (m_damageAudio != null)
		{
			audioSource.PlayOneShot(m_damageAudio);
		}
		if (m_animator != null)
		{
			m_animator.SetTrigger("hurt");
		}
		bool isDead = Mathf.Approximately(m_currentHP, 0.0f);
		if (m_character != null)
		{
			m_character.OnDamage(source);
		}

		// death/despawn
		if (isDead)
		{
			if (m_character != null)
			{
				isDead = m_character.OnDeath();
				if (m_deathAudio != null)
				{
					audioSource.PlayOneShot(m_deathAudio);
				}
			}
			else
			{
				Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
				if (m_deathAudio != null)
				{
					AudioSource.PlayClipAtPoint(m_deathAudio, transform.position); // NOTE that we can't use audioSource since we're despawning immediately // TODO: efficiency? hide & delay despawn until after audio?
				}
			}
		}

		// invincibility period
		m_invincible = true;
		if (!isDead)
		{
			// TODO: disable from animation trigger & make timer a fallback?
			Simulation.Schedule<EnableDamage>(m_invincibilityTime).m_health = this;
			StartCoroutine(InvincibilityBlink(m_invincibilityTime - m_blinkSecondsPost));
		}

		SyncUI();

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
	public void Respawn()
	{
		IncrementInternal(m_maxHP - m_currentHP);
		SyncUI();
		m_invincible = false;
	}


	void Start()
	{
		m_animator = GetComponent<Animator>();
		m_character = GetComponent<KinematicCharacter>();
		m_currentHP = m_maxHP;
		SyncUI();
	}


	private bool IncrementInternal(float diff)
	{
		float hpPrev = m_currentHP;
		m_currentHP = Mathf.Clamp(m_currentHP + diff, 0.0f, m_maxHP);

		// NOTE that color is adjusted here rather than in SyncUI() to avoid stomping boss start color
		Color color = m_gradient.Evaluate(1.0f - m_currentHP / m_maxHP);
		foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>().Where(renderer => renderer.GetComponent<IAttachable>() == null))
		{
			renderer.color = color;
		}

		return !Mathf.Approximately(m_currentHP, hpPrev);
	}

	private void SyncUI()
	{
		if (m_healthUIParent == null)
		{
			return;
		}

		// get current UI count
		int uiHealthCount = m_healthUIParent.transform.childCount - 1; // NOTE that we assume that the first child is a deactivated template object
		Assert.IsTrue(uiHealthCount >= 0);

		// remove excess
		for (; uiHealthCount > m_maxHP; --uiHealthCount)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = m_healthUIParent.transform.GetChild(uiHealthCount).gameObject;
		}

		// add deficient
		GameObject templateObj = m_healthUIParent.transform.GetChild(0).gameObject;
		RectTransform rectTf = templateObj.GetComponent<RectTransform>();
		float templateWidth = rectTf.sizeDelta.x;
		float xInc = (templateWidth + m_UIPadding) * (rectTf.anchorMin.x.FloatEqual(1.0f) ? -1.0f : 1.0f);
		float xItr = xInc * uiHealthCount;
		Assert.IsFalse(templateObj.activeSelf);
		for (; uiHealthCount < m_maxHP; ++uiHealthCount, xItr += xInc)
		{
			GameObject uiNew = Instantiate(templateObj, m_healthUIParent.transform);
			uiNew.GetComponent<RectTransform>().anchoredPosition += new Vector2(xItr, 0.0f);
			uiNew.SetActive(true);
		}

		// set sprites
		for (int i = 0; i < m_maxHP; ++i)
		{
			Image image = m_healthUIParent.transform.GetChild(i + 1).GetComponent<Image>(); // NOTE +1 due to template object
			bool notEmpty = i < m_currentHP;
			image.sprite = notEmpty ? m_healthSprite : m_healthMissingSprite;
			image.fillAmount = notEmpty ? Mathf.Clamp01(m_currentHP - i) : 1.0f;
		}

		// TODO: adjust size of parent if its width is ever visible/used
	}

	private System.Collections.IEnumerator InvincibilityBlink(float secondsMax)
	{
		float timeMax = Time.time + secondsMax;
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
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
