using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;


[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D), typeof(AudioSource), typeof(Collider2D)), /*RequireComponent(typeof(SpriteRenderer), typeof(TrailRenderer))*/] // NOTE that we assume {Trail/Sprite}Renderer are present, but can be on child object(s)
public sealed class ItemController : MonoBehaviour, IInteractable, IAttachable, IKey, ISavable
{
	[SerializeField] private float m_interactPriority = 1.0f;

	[TextArea] public string m_tooltip;

	public SwingInfo m_swingInfo = new() {
		m_angularNewtonmeters = 150.0f,
		m_linearNewtons = 0.5f,
		m_aimSpringDampPct = 0.25f,
		m_radiusSpringDampPct = 0.5f,
		m_damageThresholdSpeed = 4.0f,
		m_damage = 1.0f
	};
	[SerializeField] private float m_damageSelf = 0.1f;
	[SerializeField] private float m_impactStrongPctScalar = 2.0f;
	public float m_restDegreesOffset = 0.0f;
	public float m_throwSpeed = 20.0f;
	public float m_vfxAlpha = 0.5f;
	public int m_healAmount = 0;
	[SerializeField] private float m_healSeconds = 3.0f;
	[SerializeField] private float m_impactAudioRepeatSeconds = 0.25f;
	[SerializeField] private UnityEngine.Rendering.Universal.Light2D m_toggleLight;
	[SerializeField] private WeightedObject<AudioClip>[] m_useSFX;

	public bool m_detachOnDamage = false; // TODO: cumulative damage threshold?
	[SerializeField] private bool m_keyDestroyAfterUse = true;

	public WeightedObject<Collider2D>[] m_perColliderDamage;


	[SerializeField] private WeightedObject<string[]>[] m_sourceTextOptions;
	public bool m_activateInventory;

	[SerializeField] private GameObject m_drawPrefab;


	public float Speed => m_holder == null ? m_body.velocity.magnitude : m_holder.Speed;

	public Vector3 TrailPosition => (m_trail == null ? transform : m_trail.transform).position;

	public bool IsSwinging => m_holder != null && m_holder.IsSwinging;

	private bool m_isCriticalPath;
	public bool IsCriticalPath { // TODO: set false for persistent keys after gate is unlocked
		get => m_isCriticalPath;
		set {
			if (m_isCriticalPath == value)
			{
				return; // avoid double-adding/removing the OnSceneLoaded delegate
			}
			m_isCriticalPath = value;
			if (m_isCriticalPath)
			{
				SceneManager.sceneLoaded += OnSceneLoaded;
			}
			else
			{
				SceneManager.sceneLoaded -= OnSceneLoaded;
			}
		}
	}


	private Rigidbody2D m_body;
	private TrailRenderer m_trail;
	private AudioSource m_audioSource;
	private Collider2D[] m_colliders;
	private SpriteRenderer m_renderer;
	private Health m_health;
	private Hazard m_hazard;

	private int m_layerIdxOrig;

	private Vector2 m_trailSizes;

	private IHolder m_holder;
	public KinematicCharacter Cause { get; private set; }

	private GameObject m_drawObjectCurrent;

	private float m_impactAudioLastTime;
	private PhysicsMaterial2D m_impactAudioLastMaterial;


	[SerializeField] private int m_savableType = -1;
	int ISavable.Type { get => m_savableType; set => m_savableType = value; }

	public IUnlockable Lock { get; set; }
	public bool IsInPlace { get; set; }

	[SerializeField]
	private bool m_isReferenceKey;


	private void Awake()
	{
		m_body = GetComponent<Rigidbody2D>();
		m_trail = GetComponentInChildren<TrailRenderer>();
		m_audioSource = GetComponent<AudioSource>();
		m_colliders = new Collider2D[m_body.attachedColliderCount];
		m_body.GetAttachedColliders(m_colliders);
		m_renderer = GetComponentInChildren<SpriteRenderer>();
		m_health = GetComponent<Health>();
		m_hazard = GetComponent<Hazard>();

		m_layerIdxOrig = gameObject.layer;

		m_holder = transform.parent == null ? null : transform.parent.GetComponent<IHolder>();
#pragma warning disable IDE0031 // NOTE that we don't use null propagation since IHolderControllers can be Unity objects as well, which don't like ?? or ?.
		SetCause(m_holder == null ? null : m_holder.Component.transform.root.GetComponent<KinematicCharacter>());
#pragma warning restore IDE0031

		Bounds aggregateBounds = m_colliders.First().bounds; // NOTE that we avoid default-initialization in case the aggregate bounds shouldn't include the origin
		foreach (Collider2D collider in m_colliders)
		{
			aggregateBounds.Encapsulate(collider.bounds);
		}
		m_trailSizes = aggregateBounds.size - (m_trail.transform.position - transform.position);
		UpdateTrailWidth(m_holder != null);
	}

	private void OnEnable()
	{
		if (m_holder != null)
		{
			StartCoroutine(((IAttachable)this).MirrorParentAlphaCoroutine()); // TODO: move into IAttachable somehow?
		}

		// prevent stale VFX when re-enabling items
		m_trail.emitting = false;
		foreach (VisualEffect vfx in GetComponentsInChildren<VisualEffect>(true))
		{
			if (vfx.enabled)
			{
				vfx.Play(); // TODO: determine why this doesn't work after arm hiding/revealing
			}
		}

		if (m_isReferenceKey)
		{
			IsInPlace = true;
		}
	}

	// TODO: combine w/ ArmController version?
	private void OnCollisionEnter2D(Collision2D collision)
	{
		List<ContactPoint2D> contacts = new();
		collision.GetContacts(contacts);
		ProcessCollision(collision.collider, collision.rigidbody, collision.relativeVelocity, collision.otherCollider, collision.otherRigidbody, contacts);
	}

	private void OnCollisionStay2D(Collision2D collision) => OnCollisionEnter2D(collision);

	private void OnTriggerEnter2D(Collider2D collider)
	{
		Collider2D colliderLocal = m_colliders.Where(collider => collider != null).OrderBy(c => Vector2.Distance(c.bounds.center, collider.bounds.center)).FirstOrDefault(); // TODO: better guess at which m_colliders[] entry is involved?
		if (colliderLocal == null || colliderLocal.isTrigger)
		{
			return;
		}
		ProcessCollision(collider, collider.attachedRigidbody, m_body.velocity, colliderLocal, m_body, null);
	}

	private void OnTriggerStay2D(Collider2D collider) => OnTriggerEnter2D(collider);

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => IsCriticalPath = false; // TODO: don't assume that once any item is taken out of its scene it is no longer required?

	private void OnDestroy() => IsCriticalPath = false; // to ensure cleanup of a possible SceneManager.sceneLoaded delegate


	public float Priority(KinematicCharacter interactor) => m_interactPriority;

	public void Interact(KinematicCharacter interactor, bool reverse) => interactor.ChildAttach(this);

	// this (although public) should only be called by IHolder.ChildAttachInternal() // TODO?
	public void AttachInternal(IHolder holder)
	{
		IAttachable.AttachInternalShared(this, holder, m_body);

		m_holder = holder;

		// update renderers in case we're getting moved across sorting layers
		// TODO: generalize to IAttachable.AttachInternalShared()?
		Renderer holderRenderer = m_holder.Component.GetComponentInParent<Renderer>(); // TODO: don't assume all holders have a Renderer?
		foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
		{
			renderer.sortingLayerID = holderRenderer.sortingLayerID;
			renderer.sortingLayerName = holderRenderer.sortingLayerName; // TODO: redundant?
		}

		// ensure non-trigger collision is enabled
		// NOTE that we don't use m_colliders[] since that can include non-collider triggers (e.g. lightbulb wires)
		// TODO: generalize to IAttachable.AttachInternalShared()?
		GetComponent<Collider2D>().isTrigger = false;

		if (m_activateInventory)
		{
			AvatarController avatar = GetComponentInParent<AvatarController>();
			if (avatar != null)
			{
				avatar.m_inventoryUI.transform.GetChild(0).GetComponent<Animator>().enabled = true; // TODO: don't assume the prompt is the first child?
			}
		}

		if (m_drawObjectCurrent != null)
		{
			// stop drawing to prevent lines getting disabled "within" backpacks
			m_drawObjectCurrent.transform.SetParent(null);
			m_drawObjectCurrent = null;
		}

		m_body.useFullKinematicContacts = true;

		if (m_hazard != null)
		{
			m_hazard.enabled = false;
		}

		UpdateTrailWidth(true);

		SetCause(holder.Component.transform.root.GetComponent<KinematicCharacter>());
	}

	public string Name => m_tooltip.Split('\n').First(); // TODO: decouple from m_tooltip?

	// this is the detachment entry point
	public void /*override*/ Detach(bool noAutoReplace)
	{
		// ensure our aim indicators don't get stuck on
		AvatarController avatar = Cause == null ? null : Cause.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.StopAiming();
			if (m_activateInventory)
			{
				avatar.m_inventoryUI.transform.GetChild(0).GetComponent<Animator>().enabled = false; // TODO: don't assume the prompt is the first child? check for any other held object w/ m_activateInventory?
			}
		}

		Health causeHealth = Cause == null ? null : Cause.GetComponent<Health>();
		if (causeHealth != null)
		{
			causeHealth.HealCancel();
		}

		UpdateTrailWidth(false);

		m_holder?.ChildDetach(this, noAutoReplace); // NOTE that m_holder is null for items attached to static geometry rather than an IHolder

		if (m_body == null) // to prevent asserts during shutdown
		{
			return;
		}
		m_body.useFullKinematicContacts = false;

		m_detachOnDamage = false;
		m_activateInventory = false; // to prevent re-activating w/ every swap/reattach
		m_holder = null;

		if (gameObject.activeSelf)
		{
			EnableVFXAndDamage(); // mostly to prevent m_cause from remaining set and allowing damage if run into fast enough
		}
	}

	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites)
	{
		// convert combination to strings
		IEnumerable<string> elements = IKey.CombinationToText(set, combination, optionIndex, startIndex, endIndex);

		// select/truncate flavor text as appropriate
		IEnumerable<string> textLines = m_sourceTextOptions.RandomWeighted();
		if (textLines.Count() > elements.Count())
		{
			int i = 0;
			textLines = textLines.Where(line => i++ < elements.Count()); // TODO: don't assume one entry per replacement element?
		}

		// combine & replace
		string aggregateText = textLines.Aggregate((a, b) => a + "\n" + b);
		foreach (string element in elements)
		{
			aggregateText = aggregateText.ReplaceFirst("#", element.Trim());
		}
		aggregateText = aggregateText.Replace("#", ""); // TODO: better cleanup of extra keys w/i a single line?

		// set
		GetComponentInChildren<TMP_Text>().text = aggregateText;
		m_tooltip += "\n" + aggregateText + "\n\n";
	}

	void IKey.Use()
	{
		IsInPlace = true;

		if (!m_keyDestroyAfterUse)
		{
			return;
		}

		if (m_holder != null)
		{
			Detach(false);
		}
		gameObject.SetActive(false);
	}

	public void Deactivate()
	{
		if (!m_keyDestroyAfterUse)
		{
			return;
		}

		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}

	void ISavable.SaveInternal(SaveWriter saveFile)
	{
	}

	void ISavable.LoadInternal(SaveReader saveFile)
	{
	}

	public void Swing(bool isRelease)
	{
		if (m_holder is ArmController arm)
		{
			arm.Swing(isRelease, false);
		}

		if (isRelease)
		{
			return;
		}

		EnableVFXAndDamage();

		PlayMovementAudio();
	}

	public bool Use(bool isPressed)
	{
		if (m_useSFX.Length > 0)
		{
			m_audioSource.PlayOneShot(m_useSFX.RandomWeighted());
		}

		if (m_healAmount > 0)
		{
			Health causeHealth = Cause.GetComponent<Health>();
			if (isPressed && Cause.IsGrounded)
			{
				bool healStarted = causeHealth.HealStart(m_healSeconds, m_healAmount, gameObject);
				if (healStarted)
				{
					return true;
				}
			}
			else if (!isPressed && causeHealth.HealInProgress)
			{
				causeHealth.HealCancel();
				return true;
			}
		}

		if (isPressed && m_toggleLight != null)
		{
			GameObject lightObj = m_toggleLight.gameObject;
			if (lightObj != gameObject)
			{
				lightObj.SetActive(!lightObj.activeSelf);
			}
			else
			{
				m_toggleLight.enabled = !m_toggleLight.enabled;
			}
			return true;
		}

		// TODO: limit "ink" to prevent too many objects?
		if (m_drawPrefab != null && isActiveAndEnabled)
		{
			if (isPressed && (m_drawObjectCurrent == null || m_drawObjectCurrent.transform.parent != transform))
			{
				m_drawObjectCurrent = Instantiate(m_drawPrefab, transform);
				m_drawObjectCurrent.transform.localPosition = new(m_renderer.localBounds.max.x, 0.0f);
			}
			else if (!isPressed && m_drawObjectCurrent != null)
			{
				m_drawObjectCurrent.transform.SetParent(null);
				m_drawObjectCurrent = null;
			}
			return true;
		}

		if (isPressed && m_hazard != null)
		{
			m_hazard.enabled = true;
			EnableCollision.TemporarilyDisableCollision(Cause.GetComponentsInChildren<Collider2D>(), m_colliders, 0.5f); // TODO: parameterize time?
			Detach(false);
			return true;
		}

		return false;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ platforms/furniture and thrower
		gameObject.layer = Cause.m_layerIgnoreOneWay.ToIndex();
		EnableCollision.TemporarilyDisableCollision(Cause.GetComponentsInChildren<Collider2D>(), m_colliders, 0.1f);

		Quaternion holderRotation = m_holder.Component.transform.rotation;
		m_body.velocity = holderRotation * Vector2.right * m_throwSpeed;
		ArmController arm = (ArmController)m_holder; // NOTE that m_holder gets unset by Detach(), but PostThrow() needs to be called afterward
		Detach(false);
		arm.PostThrow();

		EnableVFXAndDamage();

		PlayMovementAudio();
	}

	public void SetCause(KinematicCharacter cause)
	{
		if (Cause == cause)
		{
			return;
		}

		Cause = cause;

		if (m_holder == null && Cause != null)
		{
			EnableVFXAndDamage();
		}

		if (Cause == null || m_trail == null)
		{
			return;
		}

		bool isFriendly = GameController.Instance.m_avatars.Contains(Cause) || (Cause is AIController ai && ai.m_friendly);
		m_trail.colorGradient = new() { colorKeys = new GradientColorKey[] { new(isFriendly ? Color.white : Color.red, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new(m_vfxAlpha, 0.0f), new(0.0f, 1.0f) } }; // NOTE that we have to replace the whole gradient rather than just setting individual attributes due to the annoying way LineRenderer's attribute setup results in local copies
	}


	private void UpdateTrailWidth(bool wide)
	{
		System.Func<float, float, float> compareFunc = wide ? Mathf.Max : Mathf.Min;
		m_trail.widthCurve = AnimationCurve.EaseInOut(0.0f, compareFunc(m_trailSizes.x, m_trailSizes.y), 1.0f, 0.0f);
	}

	private void ProcessCollision(Collider2D collider, Rigidbody2D rigidbody, Vector2 relativeVelocity, Collider2D colliderLocal, Rigidbody2D bodyLocal, List<ContactPoint2D> contacts)
	{
		if (!gameObject.activeSelf) // e.g. we're a bomb that has just gone off
		{
			DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.Deactivated);
			return;
		}

		// get reused info
		bool isDetached = m_holder == null;
		GameObject mainObj = rigidbody == null ? collider.gameObject : rigidbody.gameObject;
		WeightedObject<Collider2D> perColliderDamageInfo = m_perColliderDamage.FirstOrDefault(c => c.m_object == colliderLocal);
		bool canDamage = Cause != null && Cause.CanDamage(mainObj) && (perColliderDamageInfo == null || perColliderDamageInfo.m_weight != 0.0f);
		KinematicObject kinematicObj = mainObj.GetComponent<KinematicObject>();
		KinematicCharacter character = kinematicObj as KinematicCharacter; // NOTE that this works since objects shouldn't ever have multiple different KinematicObject-derived components

		// maybe attach to character
		// TODO: extend to BackpackController as well?
		if (isDetached && !canDamage && character != null && character.IsPickingUp && character.GetComponentsInChildren<ItemController>(true).Length < character.HoldCountMax) // NOTE that we prevent collision-catching dangerous projectiles, but they can still be caught if the button is pressed with perfect timing when the object becomes the avatar's focus (see AvatarController.OnInteract()) or if it is a secondary (non-damaging) collider making collision
		{
			bool attached = character.ChildAttach(this);
			if (attached)
			{
				DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.Attach);
				return;
			}
		}

		// if still and supported, set our layer to whatever we're resting on and sleep
		// TODO: better logic for determining when about to sleep? don't assume only one collision at a time?
		float collisionSpeed = (relativeVelocity + (kinematicObj == null ? Vector2.zero : -kinematicObj.velocity)).magnitude + Speed;
		if (isDetached && collisionSpeed < Physics2D.linearSleepTolerance)
		{
			ContactPoint2D supportingContact = contacts == null ? default : contacts.FirstOrDefault(contact => contact.normal.y > 0.0f); // TODO: handle multiple supporting contacts? better support angle?
			if (supportingContact.collider != null && supportingContact.collider.gameObject.layer != GameController.Instance.m_layerWalls.ToIndex())
			{
				gameObject.layer = supportingContact.collider.gameObject.layer;
				bodyLocal.Sleep(); // NOTE that changing the object's layer wakes it, so we have to manually Sleep() here
			}
			DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.Supported);
			return;
		}

		if (colliderLocal.ShouldIgnore(rigidbody, new[] { collider }, oneWayTopEpsilon: isDetached ? 0.1f : float.MaxValue))
		{
			DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.Ignored);
			return;
		}

		// ignore non-destructible trigger/static objects when held
		Health otherHealth = collider.ToHealth();
		if (!isDetached && otherHealth == null && (collider.isTrigger || rigidbody == null || rigidbody.bodyType != RigidbodyType2D.Dynamic))
		{
			DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.Static);
			return;
		}

		// maybe play audio
		float sfxThresholdSpeed = isDetached ? m_swingInfo.m_damageThresholdSpeed * 0.5f : m_swingInfo.m_damageThresholdSpeed; // TODO: parameterize?
		if (m_audioSource.enabled && collisionSpeed > sfxThresholdSpeed)
		{
			PhysicsMaterial2D material1 = collider.sharedMaterial != null || rigidbody == null ? collider.sharedMaterial : rigidbody.sharedMaterial;
			if (material1 != m_impactAudioLastMaterial || m_impactAudioLastTime + m_impactAudioRepeatSeconds <= Time.time) // TODO: also compare local material for multi-material objects?
			{
				PhysicsMaterial2D material2 = colliderLocal.sharedMaterial != null || bodyLocal == null ? colliderLocal.sharedMaterial : bodyLocal.sharedMaterial;
				MaterialPairInfo info = GameController.Instance.m_materialSystem.PairBestMatch(material1, material2);
				m_audioSource.PlayOneShot(info.m_collisionStrongAudio.Length > 0 && collisionSpeed >= sfxThresholdSpeed * m_impactStrongPctScalar ? info.m_collisionStrongAudio.RandomWeighted() : info.m_collisionAudio.Random());
				m_impactAudioLastTime = Time.time;
				m_impactAudioLastMaterial = material1;
			}
		}

		// check speed
		if (collisionSpeed > m_swingInfo.m_damageThresholdSpeed) // TODO: more generous condition for avatars?
		{
			// if from a valid source, apply damage/detachment
			if (canDamage)
			{
				if (otherHealth != null)
				{
					otherHealth.Decrement(Cause != null ? Cause.gameObject : gameObject, gameObject, perColliderDamageInfo != null ? perColliderDamageInfo.m_weight : m_swingInfo.m_damage, m_swingInfo.m_damageType);
				}
				if (m_health != null && !collider.isTrigger)
				{
					m_health.Decrement(gameObject, collider.gameObject, m_damageSelf, Health.DamageType.Generic);
				}
				if (m_detachOnDamage && !collider.isTrigger)
				{
					Detach(true);
				}
				if (collider.TryGetComponent(out ItemController otherItem))
				{
					if (otherItem.m_detachOnDamage)
					{
						otherItem.Detach(true);
					}
					if (otherItem.transform.parent == null && m_trail.emitting)
					{
						// reflect!
						// TODO: also set layer? deliberately add speed?
						otherItem.SetCause(Cause);
					}
				}
				DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.Damage);
			}
			else
			{
				DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.CantDamage);
			}
		}
		else
		{
			DebugEvent(collider, contacts, ConsoleCommands.ItemDebugLevels.TooSlow);
		}

		if (isDetached && !collider.isTrigger)
		{
			// set layer back to default to re-enable default collisions
			gameObject.layer = m_layerIdxOrig;
		}
	}

	private void EnableVFXAndDamage()
	{
		m_trail.emitting = true;
		StartCoroutine(UpdateVFXAndCause()); // TODO: prevent multiple instances at once?
	}

	private IEnumerator UpdateVFXAndCause()
	{
		while (true)
		{
			if (!IsSwinging && Speed < m_swingInfo.m_damageThresholdSpeed)
			{
				if (m_trail != null)
				{
					m_trail.emitting = false;
				}
				if (m_holder == null)
				{
					SetCause(null);
				}
				break;
			}
			yield return null;
		}
	}

	private void PlayMovementAudio()
	{
		if (!m_audioSource.enabled)
		{
			return;
		}
		m_audioSource.PlayOneShot(GameController.Instance.m_materialSystem.Find(m_colliders.First(c => c != null).sharedMaterial).m_movementAudio.Random()); // TODO: don't assume first collider is main material?
	}


#if UNITY_EDITOR
	private readonly List<System.Tuple<Vector2, ConsoleCommands.ItemDebugLevels, float>> m_debugEvents = new();

	private void DebugEvent(Collider2D collider, List<ContactPoint2D> contacts, ConsoleCommands.ItemDebugLevels type)
	{
		Vector2 pos = contacts != null && contacts.Count > 0 ? contacts.First().point : (transform.position + collider.transform.position) * 0.5f;
		const float lifetimeSeconds = 5.0f; // TODO: parameterize?
		m_debugEvents.Add(System.Tuple.Create(pos, type, Time.time + lifetimeSeconds));
	}

	private void OnDrawGizmos()
	{
		// draw
		if (ConsoleCommands.ItemDebugLevel != (int)ConsoleCommands.ItemDebugLevels.None)
		{
			foreach (System.Tuple<Vector2, ConsoleCommands.ItemDebugLevels, float> evt in m_debugEvents)
			{
				if ((int)evt.Item2 < ConsoleCommands.ItemDebugLevel) // TODO: draw Scene panel w/ individual checkboxes?
				{
					continue;
				}
				Color color = Color.black;
				switch (evt.Item2)
				{
					case ConsoleCommands.ItemDebugLevels.Deactivated: color = Color.gray; break;
					case ConsoleCommands.ItemDebugLevels.Supported: color = Color.yellow; break;
					case ConsoleCommands.ItemDebugLevels.Ignored: color = Color.white; break;
					case ConsoleCommands.ItemDebugLevels.Static: color = Color.blue; break;
					case ConsoleCommands.ItemDebugLevels.Attach: color = Color.cyan; break;
					case ConsoleCommands.ItemDebugLevels.TooSlow: color = Color.green; break;
					case ConsoleCommands.ItemDebugLevels.CantDamage: color = Color.magenta; break;
					case ConsoleCommands.ItemDebugLevels.Damage: color = Color.red; break;
					default: Debug.LogWarning("Unhandled ItemDebugLevel"); break;
				}

				const float radius = 0.025f; // TODO: parameterize?
				using (new UnityEditor.Handles.DrawingScope(color))
				{
					UnityEditor.Handles.DrawWireArc(evt.Item1, Vector3.forward, Vector3.right, 360.0f, radius);
				}
			}
		}

		// clean
		while (m_debugEvents.Count > 0 && m_debugEvents.First().Item3 < Time.time)
		{
			m_debugEvents.RemoveAt(0);
		}
	}

#else
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "required depending on compile configuration")]
	void DebugEvent(params object[] ignored) {}
#endif
}
