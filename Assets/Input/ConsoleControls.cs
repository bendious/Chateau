//------------------------------------------------------------------------------
// <auto-generated>
//     This code was auto-generated by com.unity.inputsystem:InputActionCodeGenerator
//     version 1.2.0
//     from Assets/Input/ConsoleControls.inputactions
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

public partial class @ConsoleControls : IInputActionCollection2, IDisposable
{
    public InputActionAsset asset { get; }
    public @ConsoleControls()
    {
        asset = InputActionAsset.FromJson(@"{
    ""name"": ""ConsoleControls"",
    ""maps"": [
        {
            ""name"": ""Console"",
            ""id"": ""72ede0bc-81c6-4878-86ad-1703f3b4a467"",
            ""actions"": [
                {
                    ""name"": ""Toggle"",
                    ""type"": ""Button"",
                    ""id"": ""430c917f-9307-4d57-ad21-5209c5a0d9a7"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Shift"",
                    ""type"": ""Button"",
                    ""id"": ""3be1c3f1-13f7-4f56-a537-73ad8caf3676"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Pause"",
                    ""type"": ""Button"",
                    ""id"": ""d3850ff8-3bd7-426d-8f4a-599b0b68e5c0"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""NeverDie"",
                    ""type"": ""Button"",
                    ""id"": ""c70ddbb3-3271-47fb-bc47-a65c1d690460"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""PassiveAI"",
                    ""type"": ""Button"",
                    ""id"": ""764a0d6d-2971-4ebd-a6b5-3b1f0b36d955"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""AIDebugLevel"",
                    ""type"": ""Button"",
                    ""id"": ""466111ba-ec3e-4b9d-b1c2-2e93675dcc02"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""RegenerateDisabled"",
                    ""type"": ""Button"",
                    ""id"": ""22fffc5b-2ccb-4c2a-b3f7-26196c2ffb47"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""SpawnEnemyWave"",
                    ""type"": ""Button"",
                    ""id"": ""fda187c4-0dde-4077-bbd0-7d96f314b65c"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""KillAllEnemies"",
                    ""type"": ""Button"",
                    ""id"": ""7b7e707e-5367-47dd-be01-5801a51f8f92"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""HarmHealAvatar"",
                    ""type"": ""Button"",
                    ""id"": ""ca69830e-716e-452b-ac54-f5b012d58769"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""SpawnEnemy"",
                    ""type"": ""Button"",
                    ""id"": ""6c776a62-737c-487a-8215-6e395f45891a"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                }
            ],
            ""bindings"": [
                {
                    ""name"": """",
                    ""id"": ""ced1bcfa-bef3-4a73-8564-db32d85f14b4"",
                    ""path"": ""<Keyboard>/backquote"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Toggle"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""ac578a68-c351-4c99-8eac-587b5aa5aaf0"",
                    ""path"": ""<Keyboard>/pause"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Pause"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""49fda13b-75ba-4b99-be0e-c9188193a464"",
                    ""path"": ""<Keyboard>/n"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""NeverDie"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""95e6556c-5b82-4575-a148-bf4d26d6b960"",
                    ""path"": ""<Keyboard>/a"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""PassiveAI"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""3c151e34-11ff-4ac9-a864-8c47ba9b19cb"",
                    ""path"": ""<Keyboard>/d"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""AIDebugLevel"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""048cfc44-f843-47f5-97b3-1a7d169a6be2"",
                    ""path"": ""<Keyboard>/r"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""RegenerateDisabled"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""b3b003f4-72bd-490f-bbc6-c6edd9baa2d0"",
                    ""path"": ""<Keyboard>/w"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""SpawnEnemyWave"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""b1649ce7-3ba6-4634-a0b4-e35e8b7712cf"",
                    ""path"": ""<Keyboard>/h"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""HarmHealAvatar"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""e685a01e-c81b-4ef1-9bc5-3ed5f4098f7d"",
                    ""path"": ""<Keyboard>/0"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=10)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""110db59d-45e1-48d5-80c3-01372243cd1f"",
                    ""path"": ""<Keyboard>/1"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""4f80d2ce-c0df-43d2-9235-d6cca973e674"",
                    ""path"": ""<Keyboard>/2"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=2)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""80f9f4e3-7684-407b-8be0-c7f618128800"",
                    ""path"": ""<Keyboard>/3"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=3)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""945974d7-c71d-4131-90ce-fab863926d6a"",
                    ""path"": ""<Keyboard>/4"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=4)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""3e6e889a-a86d-4a33-8f4c-0d349e2a5c03"",
                    ""path"": ""<Keyboard>/5"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=5)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""b2bce110-cf74-475b-893f-b96e1c006c92"",
                    ""path"": ""<Keyboard>/6"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=6)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""90c99b2d-493c-40dc-bdaa-3338cf8f190f"",
                    ""path"": ""<Keyboard>/7"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=7)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""494d7ec5-d5c8-4b9e-be92-b4522518d452"",
                    ""path"": ""<Keyboard>/8"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=8)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""c33e1ae7-fa8a-486a-af2d-de8d616e2004"",
                    ""path"": ""<Keyboard>/9"",
                    ""interactions"": """",
                    ""processors"": ""Scale(factor=9)"",
                    ""groups"": """",
                    ""action"": ""SpawnEnemy"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""42610a8d-afd2-444c-bd70-8de62e2ce5a8"",
                    ""path"": ""<Keyboard>/k"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""KillAllEnemies"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""421b7f62-ff4b-4b33-bd4e-85f0d69e3924"",
                    ""path"": ""<Keyboard>/shift"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Shift"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                }
            ]
        }
    ],
    ""controlSchemes"": []
}");
        // Console
        m_Console = asset.FindActionMap("Console", throwIfNotFound: true);
        m_Console_Toggle = m_Console.FindAction("Toggle", throwIfNotFound: true);
        m_Console_Shift = m_Console.FindAction("Shift", throwIfNotFound: true);
        m_Console_Pause = m_Console.FindAction("Pause", throwIfNotFound: true);
        m_Console_NeverDie = m_Console.FindAction("NeverDie", throwIfNotFound: true);
        m_Console_PassiveAI = m_Console.FindAction("PassiveAI", throwIfNotFound: true);
        m_Console_AIDebugLevel = m_Console.FindAction("AIDebugLevel", throwIfNotFound: true);
        m_Console_RegenerateDisabled = m_Console.FindAction("RegenerateDisabled", throwIfNotFound: true);
        m_Console_SpawnEnemyWave = m_Console.FindAction("SpawnEnemyWave", throwIfNotFound: true);
        m_Console_KillAllEnemies = m_Console.FindAction("KillAllEnemies", throwIfNotFound: true);
        m_Console_HarmHealAvatar = m_Console.FindAction("HarmHealAvatar", throwIfNotFound: true);
        m_Console_SpawnEnemy = m_Console.FindAction("SpawnEnemy", throwIfNotFound: true);
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(asset);
    }

    public InputBinding? bindingMask
    {
        get => asset.bindingMask;
        set => asset.bindingMask = value;
    }

    public ReadOnlyArray<InputDevice>? devices
    {
        get => asset.devices;
        set => asset.devices = value;
    }

    public ReadOnlyArray<InputControlScheme> controlSchemes => asset.controlSchemes;

    public bool Contains(InputAction action)
    {
        return asset.Contains(action);
    }

    public IEnumerator<InputAction> GetEnumerator()
    {
        return asset.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Enable()
    {
        asset.Enable();
    }

    public void Disable()
    {
        asset.Disable();
    }
    public IEnumerable<InputBinding> bindings => asset.bindings;

    public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false)
    {
        return asset.FindAction(actionNameOrId, throwIfNotFound);
    }
    public int FindBinding(InputBinding bindingMask, out InputAction action)
    {
        return asset.FindBinding(bindingMask, out action);
    }

    // Console
    private readonly InputActionMap m_Console;
    private IConsoleActions m_ConsoleActionsCallbackInterface;
    private readonly InputAction m_Console_Toggle;
    private readonly InputAction m_Console_Shift;
    private readonly InputAction m_Console_Pause;
    private readonly InputAction m_Console_NeverDie;
    private readonly InputAction m_Console_PassiveAI;
    private readonly InputAction m_Console_AIDebugLevel;
    private readonly InputAction m_Console_RegenerateDisabled;
    private readonly InputAction m_Console_SpawnEnemyWave;
    private readonly InputAction m_Console_KillAllEnemies;
    private readonly InputAction m_Console_HarmHealAvatar;
    private readonly InputAction m_Console_SpawnEnemy;
    public struct ConsoleActions
    {
        private @ConsoleControls m_Wrapper;
        public ConsoleActions(@ConsoleControls wrapper) { m_Wrapper = wrapper; }
        public InputAction @Toggle => m_Wrapper.m_Console_Toggle;
        public InputAction @Shift => m_Wrapper.m_Console_Shift;
        public InputAction @Pause => m_Wrapper.m_Console_Pause;
        public InputAction @NeverDie => m_Wrapper.m_Console_NeverDie;
        public InputAction @PassiveAI => m_Wrapper.m_Console_PassiveAI;
        public InputAction @AIDebugLevel => m_Wrapper.m_Console_AIDebugLevel;
        public InputAction @RegenerateDisabled => m_Wrapper.m_Console_RegenerateDisabled;
        public InputAction @SpawnEnemyWave => m_Wrapper.m_Console_SpawnEnemyWave;
        public InputAction @KillAllEnemies => m_Wrapper.m_Console_KillAllEnemies;
        public InputAction @HarmHealAvatar => m_Wrapper.m_Console_HarmHealAvatar;
        public InputAction @SpawnEnemy => m_Wrapper.m_Console_SpawnEnemy;
        public InputActionMap Get() { return m_Wrapper.m_Console; }
        public void Enable() { Get().Enable(); }
        public void Disable() { Get().Disable(); }
        public bool enabled => Get().enabled;
        public static implicit operator InputActionMap(ConsoleActions set) { return set.Get(); }
        public void SetCallbacks(IConsoleActions instance)
        {
            if (m_Wrapper.m_ConsoleActionsCallbackInterface != null)
            {
                @Toggle.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnToggle;
                @Toggle.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnToggle;
                @Toggle.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnToggle;
                @Shift.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnShift;
                @Shift.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnShift;
                @Shift.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnShift;
                @Pause.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnPause;
                @Pause.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnPause;
                @Pause.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnPause;
                @NeverDie.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnNeverDie;
                @NeverDie.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnNeverDie;
                @NeverDie.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnNeverDie;
                @PassiveAI.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnPassiveAI;
                @PassiveAI.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnPassiveAI;
                @PassiveAI.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnPassiveAI;
                @AIDebugLevel.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnAIDebugLevel;
                @AIDebugLevel.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnAIDebugLevel;
                @AIDebugLevel.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnAIDebugLevel;
                @RegenerateDisabled.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnRegenerateDisabled;
                @RegenerateDisabled.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnRegenerateDisabled;
                @RegenerateDisabled.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnRegenerateDisabled;
                @SpawnEnemyWave.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnSpawnEnemyWave;
                @SpawnEnemyWave.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnSpawnEnemyWave;
                @SpawnEnemyWave.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnSpawnEnemyWave;
                @KillAllEnemies.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnKillAllEnemies;
                @KillAllEnemies.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnKillAllEnemies;
                @KillAllEnemies.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnKillAllEnemies;
                @HarmHealAvatar.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnHarmHealAvatar;
                @HarmHealAvatar.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnHarmHealAvatar;
                @HarmHealAvatar.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnHarmHealAvatar;
                @SpawnEnemy.started -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnSpawnEnemy;
                @SpawnEnemy.performed -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnSpawnEnemy;
                @SpawnEnemy.canceled -= m_Wrapper.m_ConsoleActionsCallbackInterface.OnSpawnEnemy;
            }
            m_Wrapper.m_ConsoleActionsCallbackInterface = instance;
            if (instance != null)
            {
                @Toggle.started += instance.OnToggle;
                @Toggle.performed += instance.OnToggle;
                @Toggle.canceled += instance.OnToggle;
                @Shift.started += instance.OnShift;
                @Shift.performed += instance.OnShift;
                @Shift.canceled += instance.OnShift;
                @Pause.started += instance.OnPause;
                @Pause.performed += instance.OnPause;
                @Pause.canceled += instance.OnPause;
                @NeverDie.started += instance.OnNeverDie;
                @NeverDie.performed += instance.OnNeverDie;
                @NeverDie.canceled += instance.OnNeverDie;
                @PassiveAI.started += instance.OnPassiveAI;
                @PassiveAI.performed += instance.OnPassiveAI;
                @PassiveAI.canceled += instance.OnPassiveAI;
                @AIDebugLevel.started += instance.OnAIDebugLevel;
                @AIDebugLevel.performed += instance.OnAIDebugLevel;
                @AIDebugLevel.canceled += instance.OnAIDebugLevel;
                @RegenerateDisabled.started += instance.OnRegenerateDisabled;
                @RegenerateDisabled.performed += instance.OnRegenerateDisabled;
                @RegenerateDisabled.canceled += instance.OnRegenerateDisabled;
                @SpawnEnemyWave.started += instance.OnSpawnEnemyWave;
                @SpawnEnemyWave.performed += instance.OnSpawnEnemyWave;
                @SpawnEnemyWave.canceled += instance.OnSpawnEnemyWave;
                @KillAllEnemies.started += instance.OnKillAllEnemies;
                @KillAllEnemies.performed += instance.OnKillAllEnemies;
                @KillAllEnemies.canceled += instance.OnKillAllEnemies;
                @HarmHealAvatar.started += instance.OnHarmHealAvatar;
                @HarmHealAvatar.performed += instance.OnHarmHealAvatar;
                @HarmHealAvatar.canceled += instance.OnHarmHealAvatar;
                @SpawnEnemy.started += instance.OnSpawnEnemy;
                @SpawnEnemy.performed += instance.OnSpawnEnemy;
                @SpawnEnemy.canceled += instance.OnSpawnEnemy;
            }
        }
    }
    public ConsoleActions @Console => new ConsoleActions(this);
    public interface IConsoleActions
    {
        void OnToggle(InputAction.CallbackContext context);
        void OnShift(InputAction.CallbackContext context);
        void OnPause(InputAction.CallbackContext context);
        void OnNeverDie(InputAction.CallbackContext context);
        void OnPassiveAI(InputAction.CallbackContext context);
        void OnAIDebugLevel(InputAction.CallbackContext context);
        void OnRegenerateDisabled(InputAction.CallbackContext context);
        void OnSpawnEnemyWave(InputAction.CallbackContext context);
        void OnKillAllEnemies(InputAction.CallbackContext context);
        void OnHarmHealAvatar(InputAction.CallbackContext context);
        void OnSpawnEnemy(InputAction.CallbackContext context);
    }
}
