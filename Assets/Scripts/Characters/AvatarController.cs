using Cinemachine;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.VFX;


/// <summary>
/// This is the main class used to implement control of the avatar.
/// </summary>
[RequireComponent(typeof(Health))]
public class AvatarController : KinematicCharacter
{
	public enum JumpState
	{
		Grounded,
		PrepareToJump,
		Jumping,
		InFlight,
		WallCling,
		Landed
	}


	public AudioClip jumpAudio;

	public GameObject m_focusIndicator;
	public ButtonPrompt m_focusPrompt;
	public GameObject m_aimObject;
	public GameObject m_inventoryUI;
	public Canvas m_overlayCanvas;

	public Vector3 m_focusPromptOffset = new(0.0f, 0.3f, -0.15f);

	[HideInInspector]
	public InteractFollow m_follower;

	[SerializeField]
	private float m_lookScreenPercentMax = 0.75f;

	[SerializeField]
	private float m_moveSpringDampTime = 0.15f;

	[SerializeField]
	private float m_secondaryDegrees = -45.0f;

	[SerializeField]
	private float m_coyoteTime = 0.15f;

	[SerializeField]
	private float m_respawnSeconds = 30.0f;


	public PlayerInput Controls { get; private set; }

	public bool IsAlive => ConsoleCommands.NeverDie || health.IsAlive;


	private float m_lightRadiusOrig;
	private float m_lightDistanceOrig;

	private JumpState jumpState = JumpState.Grounded;
	private Health health;

	private bool controlEnabled = true; // TODO: remove?

	private float m_leftGroundTime = -1.0f;

	private GameObject m_focusObj;

	private bool m_looking;

	private VisualEffect m_aimVfx;
	private bool m_aiming;

	private Vector2 m_moveDesired;
	private Vector2 m_moveVel;

	private bool m_usingMouse;
	private Vector2 m_mousePosPixels;
	private Vector2 m_analogCurrent;
	private Vector2 m_analogRecent;

	private int m_inventoryIdx = -1;
	private bool m_inventoryDrag;

	// TODO: class for ease of VFX ID use?
	private static int m_spriteID;
	private static int m_sizeID;
	private static int m_spawnOffsetID;
	private static int m_speedID;
	private static int m_forwardID;
	private static int m_forwardToUpID;


	private float FocusDistance => ((CircleCollider2D)m_collider).radius;
	private float FocusRadius => FocusDistance * 2.0f; // TODO: parameterize?
	private Vector2 FocusCollectPos => (Vector2)transform.position + ((Vector2)m_aimObject.transform.position - (Vector2)transform.position).normalized * FocusDistance;
	private Vector2 FocusPriorityPos => m_usingMouse ? Camera.main.ScreenToWorldPoint(m_mousePosPixels) : m_aimObject.transform.position;

	private bool AnalogCurrentIsValid => m_analogCurrent.magnitude > 0.5f || Vector2.Dot(m_analogCurrent, m_analogRecent) > 0.0f;


	protected override void Awake()
	{
		base.Awake();
		DontDestroyOnLoad(gameObject);

		m_focusIndicator.transform.SetParent(null);
		m_focusPrompt.transform.SetParent(null);
		m_aimObject.transform.SetParent(null);

		health = GetComponent<Health>();

		m_aimVfx = GetComponent<VisualEffect>();
		m_spriteID = Shader.PropertyToID("Sprite");
		m_sizeID = Shader.PropertyToID("Size");
		m_spawnOffsetID = Shader.PropertyToID("SpawnOffset");
		m_speedID = Shader.PropertyToID("Speed");
		m_forwardID = Shader.PropertyToID("Forward");
		m_forwardToUpID = Shader.PropertyToID("ForwardToUpRadians");

		InventorySync();

		Controls = GetComponent<PlayerInput>();
		Controls.actions.FindActionMap("AlwaysOn").Enable();

		ObjectDespawn.OnExecute += OnObjectDespawn;

		Light2D light = GetComponent<Light2D>();
		m_lightRadiusOrig = light.pointLightOuterRadius;
		m_lightDistanceOrig = light.normalMapDistance;
		ScaleLight();
	}

	protected override void Update()
	{
		if (controlEnabled)
		{
			// update velocity
			move = move.SmoothDamp(m_moveDesired, ref m_moveVel, m_moveSpringDampTime);
		}
		else
		{
			move = Vector2.zero;
		}
		UpdateJumpState();
		base.Update();
	}

	protected override void FixedUpdate()
	{
		if (controlEnabled)
		{
			// determine aim position(s)
			Vector2 aimPctsFromCenter = AnalogCurrentIsValid ? m_analogCurrent : m_analogRecent;
			Camera camera = Camera.main; // TODO: cache?
			if (m_usingMouse)
			{
				Vector2 cameraExtentsPixels = camera.rect.size * new Vector2(Screen.width, Screen.height) * 0.5f;
				aimPctsFromCenter = (m_mousePosPixels - (Vector2)camera.WorldToScreenPoint(transform.position)) / cameraExtentsPixels;
			}
			aimPctsFromCenter = aimPctsFromCenter.Clamp(-1.0f, 1.0f);
			CinemachineVirtualCamera vCam = GameController.Instance.m_virtualCamera;
			Vector2 screenSizeWS = vCam == null ? Vector2.zero : new Vector2(vCam.m_Lens.OrthographicSize * vCam.m_Lens.Aspect, vCam.m_Lens.OrthographicSize) * 2.0f;
			Vector2 aimPosConstrained = transform.position + (Vector3)(screenSizeWS * aimPctsFromCenter * (m_looking ? m_lookScreenPercentMax : 0.5f));
			Vector2 aimPos = m_usingMouse ? camera.ScreenToWorldPoint(m_mousePosPixels) : aimPosConstrained;

			// aim camera/sprite
			m_aimObject.transform.position = aimPosConstrained;
			m_aimDir = aimPos.x > transform.position.x ? 1 : -1;

			// aim arms/items
			// primary aim
			ArmController[] arms = GetComponentsInChildren<ArmController>();
			ArmController primaryArm = arms.Length == 0 ? null : arms.First().transform.childCount > 0 || (arms.Last().transform.childCount == 0 && !LeftFacing) ? arms.First() : arms.Last();
			if (primaryArm != null)
			{
				primaryArm.UpdateAim(m_armOffset, aimPos, aimPos);
			}

			// secondary hold
			Vector3 secondaryAimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, LeftFacing ? 180.0f - m_secondaryDegrees : m_secondaryDegrees) * Vector3.right;
			for (int i = 0; i < arms.Length; ++i)
			{
				if (arms[i] == primaryArm)
				{
					continue;
				}
				arms[i].UpdateAim(m_armOffset, secondaryAimPos, aimPos);
			}

			// aim VFX
			if (m_aimVfx.enabled)
			{
				ItemController primaryItem = GetComponentInChildren<ItemController>();
				Vector3 forward = (aimPos - (Vector2)transform.position).normalized;
				m_aimVfx.SetVector3(m_forwardID, forward);
				m_aimVfx.SetFloat(m_forwardToUpID, primaryItem.transform.rotation.eulerAngles.z * Mathf.Deg2Rad + Mathf.PI * 0.5f - Mathf.Atan2(forward.y, forward.x));
				m_aimVfx.SetVector3(m_spawnOffsetID, primaryItem.TrailPosition - transform.position);
			}
		}

		base.FixedUpdate();
	}

	private void LateUpdate()
	{
		if (!controlEnabled)
		{
			return;
		}

		// collect possible focus objects
		m_focusObj = null;
		Collider2D[] focusCandidates = Physics2D.OverlapCircleAll(FocusCollectPos, FocusRadius); // TODO: restrict to certain layers?

		// determine current focus object
		// TODO: more nuanced prioritization?
		Vector2 priorityPos = FocusPriorityPos;
		float distSqFocus = float.MaxValue;
		bool focusCanInteract = false;
		float focusYMax = float.MinValue;
		foreach (Collider2D candidate in focusCandidates)
		{
			if (ShouldIgnore(candidate.GetComponent<Rigidbody2D>(), new Collider2D[] { candidate }, false, 0.0f, null, true))
			{
				continue; // ignore ourself / attached/ignored objects
			}

			// prioritize interactable objects
			IInteractable candidateInteract = candidate.GetComponent<IInteractable>();
			bool candidateCanInteract = candidateInteract != null && candidateInteract.CanInteract(this);

			// prioritize by mouse position
			float distSqCur = (priorityPos - (Vector2)candidate.transform.position).sqrMagnitude;

			if (candidateCanInteract && !focusCanInteract || ((candidateCanInteract || !focusCanInteract) && distSqCur < distSqFocus))
			{
				focusCanInteract = candidateCanInteract;
				distSqFocus = distSqCur;
				m_focusObj = candidate.gameObject;
				focusYMax = candidate.bounds.max.y;
			}
		}

		// place focus indicator if appropriate
		if (focusCanInteract)
		{
			m_focusIndicator.transform.SetPositionAndRotation(m_focusObj.transform.position, m_focusObj.transform.rotation);

			// mirror sprite rendering
			SpriteRenderer rendererIndicator = m_focusIndicator.GetComponent<SpriteRenderer>();
			SpriteRenderer rendererOrig = m_focusObj.GetComponent<SpriteRenderer>();
			rendererIndicator.enabled = rendererOrig != null;
			if (rendererIndicator.enabled)
			{
				rendererIndicator.sortingLayerName = rendererOrig.sortingLayerName;
				rendererIndicator.sortingLayerID = rendererOrig.sortingLayerID;
				rendererIndicator.sortingOrder = rendererOrig.sortingOrder + 1;
				rendererIndicator.sprite = rendererOrig.sprite;
				rendererIndicator.drawMode = rendererOrig.drawMode;
				rendererIndicator.size = rendererOrig.size;
				rendererIndicator.color = rendererOrig.color * 2.0f; // TODO: ensure good visibility on nearly-white/black objects and avoid jarring change to 50% gray objects
				rendererIndicator.flipX = rendererOrig.flipX;
				rendererIndicator.flipY = rendererOrig.flipY; // NOTE that items that have been dropped may have been left "backwards"
				rendererIndicator.maskInteraction = rendererOrig.maskInteraction;
			}

			// mirror sprite masking
			SpriteMask maskIndicator = m_focusIndicator.GetComponent<SpriteMask>();
			SpriteMask maskOrig = m_focusObj.GetComponent<SpriteMask>();
			maskIndicator.enabled = maskOrig != null;
			if (maskIndicator.enabled)
			{
				maskIndicator.sprite = maskOrig.sprite;
				maskIndicator.isCustomRangeActive = maskOrig.isCustomRangeActive;
				maskIndicator.frontSortingLayerID = maskOrig.frontSortingLayerID;
				maskIndicator.frontSortingOrder = maskOrig.frontSortingOrder + 1;
				maskIndicator.backSortingLayerID = maskOrig.backSortingLayerID;
				maskIndicator.backSortingOrder = maskOrig.backSortingOrder + 1;
			}

			m_focusIndicator.transform.localScale = m_focusObj.transform.localScale; // NOTE that w/o this, swapping between renderer draw modes was doing weird things to the indicator's scale...

			m_focusPrompt.transform.position = new Vector3(m_focusIndicator.transform.position.x, focusYMax, m_focusIndicator.transform.position.z) + m_focusPromptOffset;
			m_focusPrompt.SetSprite(m_focusObj.GetComponent<IInteractable>().CanInteractReverse(this) ? 1 : 0);
		}
		m_focusIndicator.SetActive(focusCanInteract);
		m_focusPrompt.gameObject.SetActive(focusCanInteract);
	}

	private void OnDestroy()
	{
		DetachAll();
		ObjectDespawn.OnExecute -= OnObjectDespawn;

		Simulation.Schedule<ObjectDespawn>().m_object = m_focusIndicator;
		Simulation.Schedule<ObjectDespawn>().m_object = m_focusPrompt.gameObject;
		Simulation.Schedule<ObjectDespawn>().m_object = m_aimObject;
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		UnityEditor.Handles.DrawWireArc(FocusCollectPos, Vector3.forward, Vector3.right, 360.0f, FocusRadius); // TODO: restrict to certain layers?
		UnityEditor.Handles.DrawWireArc(FocusPriorityPos, Vector3.forward, Vector3.right, 360.0f, 0.1f);
	}
#endif


	// called by InputSystem / PlayerInput component
	public void OnMove(InputValue input)
	{
		m_moveDesired = input.Get<Vector2>();
	}

	// called by InputSystem / PlayerInput component
	public void OnLook(InputValue input)
	{
		Vector2 value = input.Get<Vector2>();
		if (!controlEnabled)
		{
			return;
		}

		// determine input source
		m_usingMouse = Controls.currentControlScheme == "Keyboard&Mouse";

		if (m_usingMouse)
		{
			if (value.sqrMagnitude > 0.0f)
			{
				m_mousePosPixels = value;
			}
		}
		else
		{
			m_analogCurrent = value;
			if (AnalogCurrentIsValid)
			{
				m_analogRecent = m_analogCurrent.normalized; // NOTE that we don't want the rest distance to be right at the character's radius since that does not aim secondary items very well
			}
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnLookToggle(InputValue input)
	{
		m_looking = input.isPressed;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnSwing(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		ItemController primaryItem = GetComponentInChildren<ItemController>();
		if (primaryItem == null)
		{
			(LeftFacing ? GetComponentsInChildren<ArmController>().Last() : GetComponentInChildren<ArmController>()).Swing(!input.isPressed);
		}
		else
		{
			primaryItem.Swing(!input.isPressed);
		}

		// cancel throwing
		// TODO: separate button to cancel a throw?
		StopAiming();
	}

	// called by InputSystem / PlayerInput component
	public void OnThrow(InputValue input)
	{
		ItemController primaryItem = GetComponentInChildren<ItemController>(true);
		if (primaryItem == null)
		{
			return;
		}

		if (input.isPressed)
		{
			// enable/initialize aim VFX
			m_aimVfx.enabled = true;
			Sprite itemSprite = primaryItem.GetComponent<SpriteRenderer>().sprite;
			m_aimVfx.SetTexture(m_spriteID, itemSprite.texture);
			Vector3 itemSize = primaryItem.GetComponents<Collider2D>().Where(collider => !primaryItem.m_nondamageColliders.Contains(collider)).Select(collider => collider.bounds).Aggregate((bbox1, bbox2) =>
			{
				bbox1.Encapsulate(bbox2);
				return bbox1;
			}).size;
			m_aimVfx.SetFloat(m_sizeID, Mathf.Max(itemSize.x, itemSize.y));
			m_aimVfx.SetFloat(m_speedID, primaryItem.m_throwSpeed);
			m_aiming = true;
		}
		else if (m_aiming)
		{
			StopAiming();
			primaryItem.Throw();
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnJump(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		if ((jumpState == JumpState.Grounded || jumpState == JumpState.WallCling || m_leftGroundTime + m_coyoteTime >= Time.time) && input.isPressed)
		{
			jumpState = JumpState.PrepareToJump;
			m_stopJump = false;
		}
		else
		{
			m_stopJump = true;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnUse(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		StopAiming(); // NOTE that we don't care whether the item to be used is currently being aimed or even exists; even if it isn't/doesn't, we treat item use - like swinging - as a throw cancellation

		ItemController[] items = GetComponentsInChildren<ItemController>(true).ToArray();
		foreach (ItemController item in items)
		{
			bool used = item.Use(input.isPressed);
			if (used)
			{
				break;
			}
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnInteract(InputValue input)
	{
		IsPickingUp = input.isPressed;
		if (controlEnabled && IsPickingUp && m_focusObj != null)
		{
			IInteractable focusInteract = m_focusObj.GetComponent<IInteractable>();
			if (focusInteract != null && focusInteract.CanInteract(this))
			{
				focusInteract.Interact(this, false);
			}
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnDrop(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		if (m_focusObj != null)
		{
			IInteractable focusInteract = m_focusObj.GetComponent<IInteractable>();
			if (focusInteract != null && focusInteract.CanInteractReverse(this))
			{
				focusInteract.Interact(this, true);
				return;
			}
		}

		ItemController primaryItem = GetComponentInChildren<ItemController>(true);
		if (primaryItem != null)
		{
			primaryItem.Detach(false);
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnInventory(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		m_inventoryDrag = false;
		if (m_inventoryIdx >= 0)
		{
			m_inventoryUI.GetComponentsInChildren<InventoryController>()[m_inventoryIdx].Deactivate();
		}
		m_inventoryIdx = m_inventoryIdx < 0 ? 0 : -1;
		if (m_inventoryIdx >= 0)
		{
			m_inventoryUI.GetComponentsInChildren<InventoryController>()[m_inventoryIdx].Activate(true);
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnNavigate(InputValue input)
	{
		if (m_inventoryIdx < 0)
		{
			return;
		}

		// navigate inventory
		InventoryController[] inventorySlots = m_inventoryUI.GetComponentsInChildren<InventoryController>();
		int nextIdx = (m_inventoryIdx + Mathf.RoundToInt(input.Get<Vector2>().x)).Modulo(inventorySlots.Length);
		if (nextIdx == m_inventoryIdx)
		{
			return;
		}
		if (m_inventoryDrag)
		{
			InventoryController slotPrev = inventorySlots[m_inventoryIdx];
			m_inventoryIdx = nextIdx; // NOTE that this has to be done BEFORE SwapWithIndex() to keep highlighting consistent
			slotPrev.SwapWithIndex(nextIdx);
		}
		else
		{
			inventorySlots[m_inventoryIdx].Deactivate();
			m_inventoryIdx = nextIdx;
			inventorySlots[m_inventoryIdx].Activate(true);
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnSubmit(InputValue input)
	{
		if (input.isPressed && m_overlayCanvas.gameObject.activeSelf)
		{
			ToggleOverlay(null, null);
		}
		else
		{
			m_inventoryDrag = input.isPressed;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnCancel(InputValue input)
	{
		if (m_overlayCanvas.gameObject.activeSelf)
		{
			ToggleOverlay(null, null);
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnScroll(InputValue input)
	{
		if (!controlEnabled)
		{
			return;
		}

		Vector2 scroll = input.Get<Vector2>();
		if (scroll.y == 0.0f)
		{
			return;
		}

		StopAiming();

		// cycle inventory
		// prep item/holder lists
		IAttachable[] attachables = GetComponentsInChildren<IAttachable>(true).Where(attachable => attachable is not IHolder).ToArray();
		if (attachables.Length == 0)
		{
			return;
		}
		IHolder[] holdersSingle = GetComponentsInChildren<IHolder>().Where(holder => holder.Component != this).ToArray();
		IAttachable[] attachablesAndEmptyArms = attachables.Length >= 2 ? attachables : holdersSingle.SelectMany(holder => // TODO: don't assume two arms?
		{
			System.Collections.Generic.List<IAttachable> heldAttachables = holder.Component.GetComponentsInChildren<IAttachable>(true).Where(attachable => attachable is not IHolder).ToList();
			return heldAttachables.Concat(Enumerable.Repeat<IAttachable>(null, holder.HoldCountMax - heldAttachables.Count));
		}).ToArray();

		// detach
		foreach (IAttachable attachable in attachables)
		{
			attachable.Detach(true);
		}

		// re-attach
		bool reverse = scroll.y > 0.0f;
		int attachablesRemaining = attachablesAndEmptyArms.Length;
		IHolder[] holdersRepeated = holdersSingle.SelectMany(holder =>
		{
			int count = Mathf.Min(holder.HoldCountMax, attachablesRemaining);
			attachablesRemaining -= count;
			return Enumerable.Repeat(holder, count);
		}).ToArray();
		int attachableIdx = reverse ? attachablesAndEmptyArms.Length - 1 : 1;
		foreach (IHolder holder in holdersRepeated)
		{
			IAttachable attachable = attachablesAndEmptyArms[attachableIdx];
			if (attachable != null)
			{
				holder.ChildAttach(attachable);
			}
			attachableIdx = (attachableIdx + 1).Modulo(holdersRepeated.Length);
		}

		InventorySync(); // TODO: lerp to new positions?
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnPause(InputValue input)
	{
		GameController.Instance.TogglePause();
	}

	public override void OnDamage(GameObject source)
	{
		base.OnDamage(source);

		// forcibly exit overlay/inventory if active
		if (m_overlayCanvas.gameObject.activeSelf)
		{
			ToggleOverlay(null, null);
		}
		if (m_inventoryIdx >= 0)
		{
			OnInventory(null);
		}
	}

	public override bool OnDeath()
	{
		if (ConsoleCommands.NeverDie || !base.OnDeath())
		{
			return false;
		}

		DeactivateAllControl();
		InventorySync(); // since KinematicCharacter.OnDeath() drops all attachables

		if (GameController.Instance.m_avatars.All(avatar => !avatar.IsAlive))
		{
			GameController.Instance.OnLastAvatarDeath();
		}
		else
		{
			// TODO: delay camera removal until end of animation?
			GameController.Instance.RemoveCameraTargets(m_aimObject.transform, transform);
			GameController.Instance.RemoveUnreachableEnemies();

			Simulation.Schedule<AvatarRespawn>(m_respawnSeconds).m_avatar = this;
		}

		return true;
	}

	public override bool CanDamage(GameObject target)
	{
		return base.CanDamage(target) && !GameController.Instance.m_avatars.Exists(avatar => avatar.gameObject == target);
	}

	protected override void DespawnSelf()
	{
		// NOTE that we purposely don't call base.DespawnSelf() since the avatar shouldn't despawn on death
	}

	// TODO: replace w/ more generic handling
	public void OnEnemyCollision(EnemyController enemy)
	{
		// temporarily disable collision to prevent getting stuck
		// TODO: disable w/ all enemies rather than just this one? ensure consistent re-enable time?
		EnableCollision.TemporarilyDisableCollision(enemy.GetComponentsInChildren<Collider2D>(), GetComponentsInChildren<Collider2D>());

		if (!enemy.CanDamage(gameObject) || enemy.m_contactDamage.FloatEqual(0.0f))
		{
			return;
		}

		bool hurt = health.Decrement(enemy.gameObject, enemy.m_contactDamage);
		if (!hurt)
		{
			return;
		}

		controlEnabled = false; // re-enabled via EnablePlayerControl() animation trigger
		Simulation.Schedule<EnableControl>(1.0f).m_avatar = this; // NOTE that this is only a timer-based fallback
	}

	public void Respawn(bool clearInventory, bool resetPosition)
	{
		m_collider.enabled = true;
		body.simulated = true;
		controlEnabled = true;

		if (clearInventory)
		{
			foreach (IAttachable attachable in GetComponentsInChildren<IAttachable>())
			{
				attachable.Detach(true); // to allow correct UI sync immediately
				Simulation.Schedule<ObjectDespawn>().m_object = attachable.Component.gameObject;
			}
			InventorySync();
		}

		health.Respawn();

		Vector3 spawnPos = gameObject.OriginToCenterY();
		if (!resetPosition)
		{
			foreach (AvatarController avatar in GameController.Instance.m_avatars)
			{
				if (avatar == this || !avatar.IsAlive)
				{
					continue;
				}
				spawnPos = GameController.Instance.RoomFromPosition(avatar.transform.position).SpawnPointRandom();
				break;
			}
		}
		Teleport(spawnPos);
		m_aimObject.transform.position = spawnPos;

		GameController.Instance.AddCameraTargets(m_aimObject.transform, transform);

		jumpState = JumpState.Grounded;

		animator.SetBool("dead", false);
		animator.SetTrigger("respawn");

		ScaleLight(); // TODO: separate OnSceneLoaded()?
	}

	public bool ToggleOverlay(SpriteRenderer sourceRenderer, string text)
	{
		GameObject overlayObj = m_overlayCanvas.gameObject;
		if (!overlayObj.activeSelf)
		{
			Image overlayImage = overlayObj.GetComponentInChildren<Image>();
			overlayImage.sprite = sourceRenderer.sprite;
			overlayImage.color = sourceRenderer.color;
			overlayObj.GetComponentInChildren<TMP_Text>().text = text;
		}
		overlayObj.SetActive(!overlayObj.activeSelf);
		Controls.SwitchCurrentActionMap(m_overlayCanvas.gameObject.activeSelf ? "UI" : "Avatar"); // TODO: account for other UI instances?
		return overlayObj.activeSelf;
	}

	// TODO: move to event handler?
	public void InventorySync()
	{
		if (!m_inventoryUI.activeSelf)
		{
			return;
		}

		GameObject templateObj = m_inventoryUI.transform.GetChild(0).gameObject;
		Assert.IsFalse(templateObj.activeSelf);

		// gather all items/slots from child holders
		Tuple<Transform, Color>[] itemInfos = GetComponentsInChildren<IHolder>().Where(holder => holder.Component != this).SelectMany(holder =>
		{
			Transform holderTf = holder.Component.transform;
			Tuple<Transform, Color>[] children = new Tuple<Transform, Color>[holder.HoldCountMax];
			Color holderColor = holderTf.GetComponent<SpriteRenderer>().color;
			for (int i = 0; i < holder.HoldCountMax; ++i)
			{
				children[i] = Tuple.Create(i < holderTf.childCount ? holderTf.GetChild(i) : null, holderColor);
			}
			return children;
		}).ToArray();

		// create/set one icon per item/slot
		RectTransform templateTf = templateObj.transform.GetComponent<RectTransform>();
		Vector3 posItr = templateTf.anchoredPosition3D;
		for (int iconIdx = 0; iconIdx < itemInfos.Length; ++iconIdx)
		{
			GameObject UIObj;
			if (iconIdx + 1 < m_inventoryUI.transform.childCount)
			{
				UIObj = m_inventoryUI.transform.GetChild(iconIdx + 1).gameObject;
			}
			else
			{
				posItr.x = templateTf.anchoredPosition.x + (templateTf.sizeDelta.x + Mathf.Abs(templateTf.anchoredPosition.x)) * iconIdx * (templateTf.anchorMin.x.FloatEqual(1.0f) ? -1.0f : 1.0f);
				UIObj = Instantiate(templateObj, Vector3.zero, Quaternion.identity, m_inventoryUI.transform);
				UIObj.transform.GetComponent<RectTransform>().anchoredPosition3D = posItr;
				UIObj.SetActive(true);
			}
			Image uiImage = UIObj.GetComponent<Image>();
			Tuple<Transform, Color> itemCur = itemInfos[iconIdx];
			bool nonEmptySlot = itemCur.Item1 != null;
			if (nonEmptySlot)
			{
				SpriteRenderer srcComp = itemCur.Item1.GetComponent<SpriteRenderer>();
				uiImage.sprite = srcComp.sprite;
				uiImage.color = srcComp.color;
			}
			else
			{
				uiImage.sprite = templateObj.GetComponent<Image>().sprite;
				uiImage.color = itemCur.Item2;
			}
			uiImage.color *= new Color(1.0f, 1.0f, 1.0f, 0.5f); // TODO: un-hardcode?
			TMP_Text text = nonEmptySlot ? itemCur.Item1.GetComponentInChildren<TMP_Text>() : null;
			TMP_Text[] uiTexts = UIObj.GetComponentsInChildren<TMP_Text>(true);
			uiTexts.First().text = text == null ? null : text.text;
			uiTexts.Last().text = nonEmptySlot ? itemCur.Item1.GetComponent<ItemController>().m_tooltip : null;
			UIObj.GetComponent<InventoryController>().m_draggable = nonEmptySlot;
		}
		for (int j = m_inventoryUI.transform.childCount - 1; j > itemInfos.Length; --j)
		{
			Transform childTf = m_inventoryUI.transform.GetChild(j);
			childTf.SetParent(null); // in case we re-sync before the despawn is completed (e.g. backpack swapping)
			Simulation.Schedule<ObjectDespawn>().m_object = childTf.gameObject;
		}
	}

	public void OnVictory()
	{
		if (!IsAlive)
		{
			return;
		}

		DeactivateAllControl();
		animator.SetTrigger("victory");
		GetComponent<Health>().m_invincible = true;
	}

	public void DeactivateAllControl()
	{
		controlEnabled = false;
		m_focusIndicator.SetActive(false);
		m_focusPrompt.gameObject.SetActive(false);
		StopAiming();
	}

	// called from animation trigger as well as the EnableControl event
	public void EnablePlayerControl()
	{
		if (IsAlive)
		{
			controlEnabled = true;
		}
	}


	private void OnObjectDespawn(ObjectDespawn evt)
	{
		if (evt.m_object.transform.root != transform)
		{
			return;
		}
		IAttachable attachable = evt.m_object.GetComponent<IAttachable>();
		if (attachable == null)
		{
			return;
		}

		StopAiming();
		attachable.Detach(false); // to swap in new items and so that we can refresh inventory immediately even though deletion hasn't happened yet
		InventorySync();
	}

	private void ScaleLight()
	{
		Light2D light = GetComponent<Light2D>();
		float scalar = GameController.Instance.m_zoneScalar;
		light.pointLightOuterRadius = scalar * m_lightRadiusOrig;
		light.NonpublicSetterWorkaround("m_NormalMapDistance", scalar * m_lightDistanceOrig);
	}

	private void StopAiming()
	{
		m_aimVfx.enabled = false; // NOTE that this instantly removes any existing particles, which is fine for this effect
		m_aiming = false;
	}

	private void UpdateJumpState()
	{
		m_jump = false;
		switch (jumpState)
		{
			case JumpState.Grounded:
				if (!IsGrounded)
				{
					jumpState = JumpState.InFlight;
					m_leftGroundTime = Time.time;
				}
				break;
			case JumpState.PrepareToJump:
				m_leftGroundTime = -1.0f;
				jumpState = JumpState.Jumping;
				m_jump = true;
				break;
			case JumpState.Jumping:
				if (!IsGrounded)
				{
					if (audioSource && jumpAudio)
					{
						audioSource.PlayOneShot(jumpAudio);
					}
					jumpState = JumpState.InFlight;
				}
				else
				{
					jumpState = JumpState.Grounded;
				}
				break;
			case JumpState.InFlight:
				if (IsGrounded)
				{
					jumpState = JumpState.Landed;
				}
				else if (IsWallClinging)
				{
					jumpState = JumpState.WallCling;
				}
				break;
			case JumpState.WallCling:
				if (!IsWallClinging)
				{
					jumpState = JumpState.InFlight;
					m_leftGroundTime = Time.time;
				}
				break;
			case JumpState.Landed:
				jumpState = JumpState.Grounded;
				break;
		}
	}
}
