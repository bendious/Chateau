﻿using Cinemachine;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.VFX;


/// <summary>
/// This is the main class used to implement control of the avatar.
/// </summary>
[RequireComponent(typeof(Health))]
public sealed class AvatarController : KinematicCharacter
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


	[SerializeField] private AudioClip m_jumpAudio;

	[SerializeField] private GameObject m_focusIndicator;
	[SerializeField] private ButtonPrompt m_focusPrompt;
	public GameObject m_aimObject;
	public GameObject m_inventoryUI;
	public Image m_progressMeterUI;

	public Vector3 m_focusPromptOffset = new(0.0f, 0.3f, -0.15f);

	[SerializeField] private LayerMaskHelper m_focusLayers;

	[HideInInspector] public InteractFollow m_follower;

	[SerializeField] private float m_lookScreenPercentMax = 0.75f;

	[SerializeField] private float m_moveSpringDampTime = 0.15f;

	[SerializeField] private float m_coyoteTime = 0.15f;
	[SerializeField] private float m_coyoteReverseTime = 0.15f;

	[SerializeField] private float m_throwIgnoreSeconds = 0.15f;

	[SerializeField] private float m_respawnSeconds = 30.0f;


	public PlayerInput Controls { get; private set; }
	public InputActionMap ControlsUI { get; private set; }

	public override bool IsAlive => ConsoleCommands.NeverDie || base.IsAlive;


	private bool ControlEnabled => m_controlEnabled && !m_controlDisabledUntilHurt;


	private JumpState jumpState = JumpState.Grounded;

	private bool m_controlEnabled = true;
	private bool m_controlDisabledUntilHurt = false;

	private float m_leftGroundTime = -1.0f;
	private float m_cachedJumpTime = -1.0f;

	private GameObject m_focusObj;

	public bool IsLooking { get; private set; }

	private VisualEffect m_aimVfx;
	private bool m_aiming;

	private float m_throwIgnoreTime;
	private GameObject m_throwObj;

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


	private float FocusDistance => Bounds.extents.x;
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

		m_aimVfx = GetComponent<VisualEffect>();
		m_spriteID = Shader.PropertyToID("Sprite");
		m_sizeID = Shader.PropertyToID("Size");
		m_spawnOffsetID = Shader.PropertyToID("SpawnOffset");
		m_speedID = Shader.PropertyToID("Speed");
		m_forwardID = Shader.PropertyToID("Forward");
		m_forwardToUpID = Shader.PropertyToID("ForwardToUpRadians");

		InventorySync();

		Controls = GetComponent<PlayerInput>();
		ControlsUI = Controls.actions.FindActionMap("UI");
		Controls.actions.FindActionMap("AlwaysOn").Enable();
		Controls.actions.FindActionMap("Avatar").FindAction("LookToggle").canceled += OnLookToggleCancel; // NOTE that this wouldn't be necessary if the Hold and Press/Release interactions worked together to allow detecting hold stop as well as start // TODO: cleaner workaround / remove hardcoding?

		OnHealthDecrement.OnExecute += OnDamage;
		OnHealthDeath.OnExecute += OnDeath;
		ObjectDespawn.OnExecute += OnObjectDespawn;
	}

	protected override void Update()
	{
		if (ControlEnabled)
		{
			// update velocity
			move = move.SmoothDamp(m_moveDesired, ref m_moveVel, m_moveSpringDampTime);
		}
		else if (IsGrounded)
		{
			move = Vector2.zero;
		}
		UpdateJumpState();
		base.Update();
	}

	protected override void FixedUpdate()
	{
		base.FixedUpdate();

		Vector2 aimPosConstrained = m_aimObject.transform.position;
		Vector2 aimPos = aimPosConstrained;

		if (ControlEnabled)
		{
			// determine aim position(s)
			Vector2 aimPctsFromCenter = AnalogCurrentIsValid ? m_analogCurrent : m_analogRecent != Vector2.zero ? m_analogRecent : (aimPos - (Vector2)transform.position).normalized; // NOTE the m_analogRecent check, to retain the current aim direction while still constraining aim distance before any valid look input has been receieved
			Camera camera = Camera.main; // TODO: cache?
			if (m_usingMouse)
			{
				Vector2 cameraExtentsPixels = camera.rect.size * new Vector2(Screen.width, Screen.height) * 0.5f;
				aimPctsFromCenter = (m_mousePosPixels - (Vector2)camera.WorldToScreenPoint(transform.position)) / cameraExtentsPixels;
			}
			aimPctsFromCenter = aimPctsFromCenter.Clamp(-1.0f, 1.0f);
			CinemachineVirtualCamera vCam = GameController.Instance.m_vCamMain;
			Vector2 screenSizeWS = vCam == null ? Vector2.zero : new Vector2(vCam.m_Lens.OrthographicSize * vCam.m_Lens.Aspect, vCam.m_Lens.OrthographicSize) * 2.0f;
			aimPosConstrained = transform.position + (Vector3)(screenSizeWS * aimPctsFromCenter * m_lookScreenPercentMax);
			aimPos = m_usingMouse ? camera.ScreenToWorldPoint(m_mousePosPixels) : aimPosConstrained;
		}

		// aim camera/sprite
		m_aimObject.transform.position = aimPosConstrained;
		AimArms(aimPos);

		// aim VFX
		if (m_aimVfx.enabled)
		{
			ItemController primaryItem = GetComponentInChildren<ItemController>();
			if (primaryItem != null)
			{
				Vector3 forward = (aimPos - (Vector2)transform.position).normalized;
				m_aimVfx.SetVector3(m_forwardID, forward);
				m_aimVfx.SetFloat(m_forwardToUpID, primaryItem.transform.rotation.eulerAngles.z * Mathf.Deg2Rad + Mathf.PI * 0.5f - Utility.ZRadians(forward));
				m_aimVfx.SetVector3(m_spawnOffsetID, primaryItem.TrailPosition - transform.position);
			}
		}
	}

	private void LateUpdate()
	{
		if (!ControlEnabled)
		{
			m_focusIndicator.SetActive(false);
			m_focusPrompt.gameObject.SetActive(false);
			return;
		}

		// collect possible focus objects
		m_focusObj = null;
		Collider2D[] focusCandidates = Physics2D.OverlapCircleAll(FocusCollectPos, FocusRadius, m_focusLayers);

		// determine current focus object
		// TODO: more nuanced prioritization?
		Vector2 priorityPos = FocusPriorityPos;
		Tuple<Collider2D, Tuple<float, float>> focus = focusCandidates.Length <= 0 ? Tuple.Create<Collider2D, Tuple<float, float>>(null, Tuple.Create(-1.0f, float.MaxValue)) : focusCandidates.SelectMinWithValue(candidate =>
		{
			if (m_colliders.All(collider => collider.ShouldIgnore(candidate.attachedRigidbody, new[] { candidate }, ignorePhysicsSystem: true)) || (candidate.gameObject == m_throwObj && m_throwIgnoreTime >= Time.time))
			{
				return Tuple.Create(-1.0f, float.MaxValue); // ignore ourself / attached/ignored/just-thrown objects
			}

			// prioritize interactable objects
			IInteractable candidateInteract = candidate.GetComponent<IInteractable>();
			float candidatePriority = candidateInteract != null && candidateInteract.CanInteract(this) ? candidateInteract.Priority(this) : 0.0f;

			// prioritize by mouse position
			float distSqCur = (priorityPos - (Vector2)candidate.transform.position).sqrMagnitude;

			return Tuple.Create(candidatePriority, distSqCur);
		}, new PriorityDistanceComparer());
		m_focusObj = focus.Item1 == null || focus.Item2.Item1 < 0.0f || focus.Item2.Item2 == float.MaxValue ? null : focus.Item1.gameObject;

		// place focus indicator if appropriate
		bool focusCanInteract = focus.Item2.Item1 > 0.0f;
		if (focusCanInteract)
		{
			// gather components from indicator and focus
			// TODO: cache indicator components?
			SpriteMask maskIndicator = m_focusIndicator.GetComponent<SpriteMask>();
			SpriteMask maskOrig = m_focusObj.GetComponent<SpriteMask>();
			SpriteRenderer rendererOrig = m_focusObj.GetComponent<SpriteRenderer>();
			SpriteRenderer rendererIndicator = m_focusIndicator.GetComponent<SpriteRenderer>();
			Light2D lightIndicator = m_focusIndicator.GetComponent<Light2D>();
			Light2D lightOrig = m_focusObj.GetComponentsInChildren<Light2D>().FirstOrDefault(light => light.lightType == Light2D.LightType.Sprite); // TODO: prevent getting attached items/etc?

			// enable/disable indicator components
			maskIndicator.enabled = maskOrig != null && rendererOrig != null && rendererOrig.maskInteraction != SpriteMaskInteraction.None;
			lightIndicator.enabled = (rendererOrig != null || lightOrig != null) && !maskIndicator.enabled && (rendererOrig == null || (rendererOrig.drawMode == SpriteDrawMode.Simple && !rendererOrig.flipX && !rendererOrig.flipY));
			rendererIndicator.enabled = rendererOrig != null && !lightIndicator.enabled;

			// set transform
			Vector3 indicatorPos = lightOrig != null && lightIndicator.enabled ? lightOrig.transform.position : m_focusObj.transform.position;
			m_focusIndicator.transform.SetPositionAndRotation(indicatorPos, lightOrig != null && lightIndicator.enabled ? lightOrig.transform.rotation : m_focusObj.transform.rotation);

			// use sprite light to encourage bloom
			if (lightIndicator.enabled)
			{
				lightIndicator.NonpublicSetterWorkaround("m_LightCookieSprite", lightOrig != null ? lightOrig.lightCookieSprite : rendererOrig.sprite);
				lightIndicator.color = lightOrig != null ? lightOrig.color : rendererOrig.color;
				lightIndicator.NonpublicSetterWorkaround("m_ApplyToSortingLayers", lightOrig != null ? lightOrig.NonpublicGetterWorkaround("m_ApplyToSortingLayers") : new[] { rendererOrig.sortingLayerID });
				// TODO: handle flip{X/Y}/maskInteraction?
			}

			// mirror sprite rendering
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

			Bounds bbox = focus.Item1.GetComponents<Collider2D>().ToBounds();
			m_focusPrompt.transform.position = new Vector3(bbox.center.x, bbox.max.y, m_focusIndicator.transform.position.z) + m_focusPromptOffset;
			m_focusPrompt.SetSprite(m_focusObj.GetComponent<IInteractable>().CanInteractReverse(this) ? 1 : 0);
		}
		m_focusIndicator.SetActive(focusCanInteract);
		m_focusPrompt.gameObject.SetActive(focusCanInteract);
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		Controls.actions.FindActionMap("Avatar").FindAction("LookToggle").canceled -= OnLookToggleCancel; // TODO: cleaner workaround / remove hardcoding?
		OnHealthDecrement.OnExecute -= OnDamage;
		OnHealthDeath.OnExecute -= OnDeath;
		ObjectDespawn.OnExecute -= OnObjectDespawn;

		if (GameController.IsSceneLoad) // NOTE that this is more about application shutdown than scene-to-scene load
		{
			return;
		}

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


	public override void Teleport(Vector3 position)
	{
		m_aimObject.transform.position = position + (m_aimObject.transform.position - transform.position); // TODO: parameterize whether to teleport the aim?
		base.Teleport(position);
	}


	// called by InputSystem / PlayerInput component
	public void OnMove(InputValue input) => m_moveDesired = input.Get<Vector2>();

	// called by InputSystem / PlayerInput component
	public void OnLook(InputValue input)
	{
		if (!ControlEnabled)
		{
			return;
		}

		// determine input source
		m_usingMouse = Controls.currentControlScheme == GameController.Instance.m_mouseControlScheme;

		Vector2 value = input.Get<Vector2>();
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
		IsLooking = ControlEnabled && input != null && input.isPressed;
		if (m_aimObject == null) // to prevent asserts during shutdown
		{
			return;
		}
		if (IsLooking)
		{
			GameController.Instance.AddCameraTargets(m_aimObject.transform);
		}
		else
		{
			GameController.Instance.RemoveCameraTargets(m_aimObject.transform);
		}
	}

	public void OnLookToggleCancel(InputAction.CallbackContext context)
	{
		OnLookToggle(null);
	}

	public void OnSwing(InputValue input)
	{
		if (!ControlEnabled)
		{
			return;
		}

		ItemController primaryItem = GetComponentInChildren<ItemController>();
		if (primaryItem == null)
		{
			PrimaryArm(GetComponentsInChildren<ArmController>()).Swing(!input.isPressed, false);
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
		if (!ControlEnabled)
		{
			return;
		}

		ItemController primaryItem = GetComponentInChildren<ItemController>();
		if (primaryItem == null)
		{
			PrimaryArm(GetComponentsInChildren<ArmController>()).Swing(!input.isPressed, true); // TODO: switch back and forth between both arms?
			return;
		}

		if (input.isPressed)
		{
			// enable/initialize aim VFX
			m_aimVfx.enabled = true;
			Sprite itemSprite = primaryItem.GetComponentInChildren<SpriteRenderer>().sprite;
			m_aimVfx.SetTexture(m_spriteID, itemSprite.texture);
			Vector3 itemSize = primaryItem.GetComponents<Collider2D>().Where(collider => !primaryItem.m_perColliderDamage.Any(info => info.m_object == collider && info.m_weight < 1.0f)).ToBounds().size;
			m_aimVfx.SetFloat(m_sizeID, Mathf.Max(itemSize.x, itemSize.y));
			m_aimVfx.SetFloat(m_speedID, primaryItem.m_throwSpeed);
			m_aiming = true;
		}
		else if (m_aiming)
		{
			StopAiming();
			primaryItem.Throw();

			m_throwIgnoreTime = Time.time + m_throwIgnoreSeconds;
			m_throwObj = primaryItem.gameObject;
		}
	}

	// called by InputSystem / PlayerInput component
	public void OnJump(InputValue input)
	{
		if (!ControlEnabled)
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
			m_cachedJumpTime = input.isPressed ? Time.time : -1.0f;
		}
	}

	// called by InputSystem / PlayerInput component
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnDash(InputValue input)
	{
		if (!ControlEnabled)
		{
			return;
		}
		m_dash = true;
	}

	public void OnUse(InputValue input)
	{
		if (!ControlEnabled)
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
		if (ControlEnabled && IsPickingUp && m_focusObj != null)
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
		if (!ControlEnabled)
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
		else
		{
			GetComponentInChildren<IAttachable>(true)?.Detach(false); // NOTE that this may invalidate m_inventoryIdx, but it will be corrected by InventorySync()
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnInventory(InputValue input)
	{
		if (!ControlEnabled)
		{
			return;
		}

		m_inventoryDrag = false;
		if (m_inventoryIdx >= 0)
		{
			m_inventoryUI.GetComponentsInChildren<InventoryController>()[m_inventoryIdx].Deactivate();
		}
		if (m_inventoryIdx < 0)
		{
			ItemController[] items = GetComponentsInChildren<ItemController>(true); // TODO: don't assume inventory is ordered the same as items?
			ItemController item = items.FirstOrDefault(item => item.m_activateInventory);
			m_inventoryIdx = item == null ? 0 : Array.IndexOf(items, item);
		}
		else
		{
			m_inventoryIdx = -1;
		}
		if (m_inventoryIdx >= 0)
		{
			m_inventoryUI.GetComponentsInChildren<InventoryController>()[m_inventoryIdx].Activate(true);
		}

		// {de}activate button prompt animation
		Image buttonPromptImage = m_inventoryUI.transform.GetChild(0).GetComponent<Image>();
		buttonPromptImage.color = new(buttonPromptImage.color.r, buttonPromptImage.color.g, buttonPromptImage.color.b, m_inventoryIdx >= 0 ? 1.0f : 0.5f);
		buttonPromptImage.GetComponent<Animator>().enabled = m_inventoryIdx >= 0;
	}

	// called by InputSystem / PlayerInput component
	public void OnNavigate(InputValue input)
	{
		// NOTE that we don't check ControlEnabled for UI-only actions
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

	public void OnSubmit(InputValue input)
	{
		// NOTE that we don't check ControlEnabled for UI-only actions
		m_inventoryDrag = input.isPressed;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnCancel(InputValue input)
	{
		// NOTE that we don't check ControlEnabled for UI-only actions
	}

	// called by InputSystem / PlayerInput component
	public void OnScroll(InputValue input)
	{
		// NOTE that we don't check ControlEnabled for UI-only actions
		if (!ControlEnabled)
		{
			return;
		}

		Vector2 scroll = input.Get<Vector2>();
		if (scroll.y == 0.0f)
		{
			return;
		}

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
			attachable.Detach(true); // TODO: specify holderNew?
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
	public void OnPause(InputValue input) => GameController.Instance.TogglePause(); // NOTE that we don't check ControlEnabled for UI-only actions


	private void OnDamage(OnHealthDecrement evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}

		// forcibly exit inventory if active
		if (m_inventoryIdx >= 0)
		{
			OnInventory(null);
		}

		m_controlDisabledUntilHurt = false;
	}

	private void OnDeath(OnHealthDeath evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}

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
	}


	public override bool CanDamage(GameObject target)
	{
		if (!base.CanDamage(target) || GameController.Instance.m_avatars.Exists(avatar => avatar.gameObject == target))
		{
			return false;
		}
		AIController ai = target.GetComponent<AIController>();
		if (ai != null && ai.m_friendly)
		{
			return false;
		}
		return true;
	}

	// TODO: CanBeDamagedBy() to exclude some types of non-character-caused damage?

	public override float TargetPriority(KinematicCharacter source, bool friendly) => !IsAlive ? 0.0f : base.TargetPriority(source, friendly);


	protected override void DespawnSelf()
	{
		// NOTE that we purposely don't call base.DespawnSelf() since the avatar shouldn't despawn on death
	}


	public void Respawn(bool clearInventory, bool resetPosition)
	{
		foreach (Collider2D collider in m_colliders)
		{
			collider.enabled = true;
		}
		body.simulated = true;

		if (clearInventory)
		{
			foreach (IAttachable attachable in GetComponentsInChildren<IAttachable>()) // NOTE that we purposely ignore disabled components, which should only occur w/i other attachables, and that we have to process all descendants due to arms being intermediate objects
			{
				attachable.Detach(true); // to allow correct UI sync immediately
				Simulation.Schedule<ObjectDespawn>().m_object = attachable.Component.gameObject;
			}
			InventorySync();
		}

		if (!IsAlive) // TODO: check if any avatar is dead?
		{
			m_health.Respawn();
		}
		m_controlDisabledUntilHurt = false;

		if (resetPosition)
		{
			Teleport(gameObject.OriginToCenterY());
		}

		GameController.Instance.AddCameraTargets(transform);

		jumpState = JumpState.Grounded;

		m_animator.SetBool("dead", false);
		m_animator.SetTrigger(resetPosition ? "respawn" : "revive");
	}

	// TODO: move to event handler?
	public void InventorySync()
	{
		if (!m_inventoryUI.activeSelf)
		{
			return;
		}

		// TODO: don't assume the template is the first deactivated object?
		GameObject templateObj = m_inventoryUI.GetComponentsInChildren<Transform>(true).Select(tf => tf.gameObject).First(obj => !obj.activeSelf);

		// gather all items/slots from child holders
		Tuple<Component, Color>[] itemInfos = GetComponentsInChildren<IHolder>().Where(holder => holder.Component != this).SelectMany(holder =>
		{
			Transform holderTf = holder.Component.transform;
			Color holderColor = holderTf.GetComponent<SpriteRenderer>().color;
			Tuple<Component, Color>[] children = holderTf.GetComponentsInDirectChildren<IAttachable>(a => a.Component, true).Select(attachable => Tuple.Create(attachable.Component, holderColor)).ToArray();
			return children.Length < holder.HoldCountMax ? children.Concat(Enumerable.Repeat(Tuple.Create<Component, Color>(null, holderColor), holder.HoldCountMax - children.Length)).ToArray() : children; // TODO: efficiency?
		}).ToArray();

		// create/set one icon per item/slot
		RectTransform templateTf = templateObj.transform.GetComponent<RectTransform>();
		int templateOffset = templateTf.GetSiblingIndex() + 1;
		Vector3 posItr = templateTf.anchoredPosition3D;
		for (int iconIdx = 0; iconIdx < itemInfos.Length; ++iconIdx)
		{
			GameObject UIObj;
			int idxOffset = iconIdx + templateOffset;
			if (idxOffset < m_inventoryUI.transform.childCount)
			{
				UIObj = m_inventoryUI.transform.GetChild(idxOffset).gameObject;
			}
			else
			{
				posItr.x = templateTf.anchoredPosition.x + (templateTf.sizeDelta.x + Mathf.Abs(templateTf.anchoredPosition.x)) * iconIdx * (templateTf.anchorMin.x.FloatEqual(1.0f) ? -1.0f : 1.0f);
				UIObj = Instantiate(templateObj, Vector3.zero, Quaternion.identity, m_inventoryUI.transform);
				UIObj.transform.GetComponent<RectTransform>().anchoredPosition3D = posItr;
				UIObj.SetActive(true);
			}
			Image uiImage = UIObj.GetComponent<Image>();
			Tuple<Component, Color> itemCur = itemInfos[iconIdx];
			bool nonEmptySlot = itemCur.Item1 != null;
			if (nonEmptySlot)
			{
				SpriteRenderer srcComp = itemCur.Item1.GetComponentInChildren<SpriteRenderer>();
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

			// TODO: move into InventoryController? update meters if active
			InventoryController inventoryController = UIObj.GetComponent<InventoryController>();
			inventoryController.m_itemText.text = text == null ? null : text.text;
			inventoryController.m_tooltipPerItem.text = nonEmptySlot ? itemCur.Item1.GetComponent<ItemController>().m_tooltip : null;
			inventoryController.m_draggable = nonEmptySlot;
		}
		int idxMax = itemInfos.Length - 1;
		for (int j = m_inventoryUI.transform.childCount - 1; j > idxMax + templateOffset; --j)
		{
			Transform childTf = m_inventoryUI.transform.GetChild(j);
			childTf.SetParent(null); // in case we re-sync before the despawn is completed (e.g. backpack swapping)
			Simulation.Schedule<ObjectDespawn>().m_object = childTf.gameObject;
		}
		m_inventoryIdx = Mathf.Min(m_inventoryIdx, idxMax);
	}

	public void OnVictory()
	{
		if (!IsAlive)
		{
			return;
		}

		m_controlDisabledUntilHurt = false;
		m_animator.SetTrigger("victory");
		GetComponent<Health>().m_invincible = true;
	}

	public void DisablePlayerControl()
	{
		m_controlEnabled = false;
		m_focusIndicator.SetActive(false);
		m_focusPrompt.gameObject.SetActive(false);
		StopAiming();
		m_health.HealCancel();
		// TODO: event to allow other delayed actions to self-cancel?
	}

	// called from animation trigger as well as the EnableControl event and Respawn()
	// NOTE that it deliberately only works when alive and is also independent of m_controlDisabledUntilHurt, which needs to persist through animation triggers
	public void EnablePlayerControl()
	{
		if (IsAlive)
		{
			m_controlEnabled = true;
		}
	}

	public void DisablePlayerControlUntilHurt() => m_controlDisabledUntilHurt = true;


	private void OnObjectDespawn(ObjectDespawn evt)
	{
		if (evt.m_object.transform.root != transform)
		{
			return;
		}
		if (!evt.m_object.TryGetComponent(out IAttachable attachable))
		{
			return;
		}

		StopAiming();
		attachable.Detach(false); // to swap in new items and so that we can refresh inventory immediately even though deletion hasn't happened yet
		InventorySync();
	}

	public void StopAiming()
	{
		m_aimVfx.enabled = false; // NOTE that this instantly removes any existing particles, which is fine for this effect
		m_aiming = false;
	}

	private void UpdateJumpState()
	{
		m_jump = 0.0f;
		switch (jumpState)
		{
			case JumpState.Grounded:
				if (IsGrounded)
				{
					if (m_cachedJumpTime + m_coyoteReverseTime >= Time.time)
					{
						goto case JumpState.PrepareToJump; // C#'s version of fallthrough; see https://stackoverflow.com/questions/174155/switch-statement-fallthrough-in-c
					}
				}
				else
				{
					jumpState = JumpState.InFlight;
					m_leftGroundTime = Time.time;
				}
				break;
			case JumpState.PrepareToJump:
				m_leftGroundTime = -1.0f;
				jumpState = JumpState.Jumping;
				m_jump = 1.0f; // TODO: support partial-strength jumps when bound to float-based input?
				break;
			case JumpState.Jumping:
				if (!IsGrounded)
				{
					if (m_audioSource && m_jumpAudio)
					{
						m_audioSource.PlayOneShot(m_jumpAudio);
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
