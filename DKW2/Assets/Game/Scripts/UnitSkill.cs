using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using RTSEngine;
using RTSEngine.EntityComponent;
using RTSEngine.UI;
using RTSEngine.Task;
using RTSEngine.Entities;
using RTSEngine.Cameras;
using RTSEngine.Utilities;
using RTSEngine.Event;
using RTSEngine.Determinism;
using System;
using RTSEngine.Attack;
using RTSEngine.Selection;
using Unity.VisualScripting;
using static UnityEngine.GraphicsBuffer;
using RTSEngine.Movement;

public class UnitSkill : EntityComponentBase
{
    enum SkillType
    {
        Cat,
        Dog
    }

    #region Attributes
    [SerializeField]
    private SkillType skillType = SkillType.Cat;
    [SerializeField]
    private float maxAbilityDistance = 2.0f;
    [SerializeField]
    private AnimatorOverrideController skillAnimation = null;
    [SerializeField]
    private AudioClip skillSound = null;

    public LayerMask m_LayerMask;

    protected IFactionEntity factionEntity { private set; get; }
    public bool IsDisabled { private set; get; }

    // DAMAGE
    [SerializeField, Tooltip("Data that defines the damage to deal in a regular attack.")]
    private DamageData damageData = new DamageData { unitMin = 10, unitMax = 10, buildingMin = 10, buildingMax = 10, custom = new CustomDamageData[0] };
    public DamageData DamageData => damageData;

    [SerializeField]
    private EntityComponentTaskUIAsset taskUI = null;

    [SerializeField, Tooltip("Enable/disable cooldown time for the attack.")]
    private GlobalTimeModifiedTimer cooldown = new GlobalTimeModifiedTimer();
    public bool IsCooldownActive => cooldown.IsActive;
    public float CurrCooldownValue => cooldown.CurrValue;

    [Header("Layers"), SerializeField, Tooltip("Input the layer's name to be used for entity selection objects.")]
    private string entitySelectionLayer = "EntitySelection";
    public string EntitySelectionLayer => entitySelectionLayer;

    // UI
    [SerializeField, Tooltip("How would the set target task look when the attack type is in cooldown?")]
    private EntityComponentLockedTaskUIData setTargetCooldownUIData = new EntityComponentLockedTaskUIData { color = Color.red, icon = null };

    // Game services
    protected ITaskManager taskMgr { private set; get; }
    protected IGlobalEventPublisher globalEvent { private set; get; }
    protected IMainCameraController mainCameraController { private set; get; }
    protected IInputManager inputMgr { private set; get; }

    [SerializeField]
    private GameObject gameObjectSkillCanvas;
    private Canvas skillCanvas;
    [SerializeField]
    private Image skillIndicatorImage;
    [SerializeField]
    private GameObject skillEffect;
    [SerializeField]
    private Collider skillCollider = null;
    protected RaycastHitter raycast;
    #endregion

    #region Raising Events
    private void OnCooldownOver()
    {
        RaiseCooldownUpdated();
        globalEvent.RaiseEntityComponentTaskUIReloadRequestGlobal(this);
    }

    public event CustomEventHandler<IEntityComponent, EventArgs> CooldownUpdated;
    private void RaiseCooldownUpdated()
    {
        Debug.Log("Cooldown Updated");
        var handler = CooldownUpdated;
        Debug.Log(handler);
        handler?.Invoke(this, EventArgs.Empty);
    }

    public event CustomEventHandler<IEntityComponent, EventArgs> ReloadUpdated;
    private void RaiseReloadUpdated()
    {
        var handler = ReloadUpdated;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public CustomEventHandler<IEntityComponent, HealthUpdateArgs> AttackDamageDealt;

    private void RaiseAttackDamageDealt(HealthUpdateArgs e)
    {
        var handler = AttackDamageDealt;
        handler?.Invoke(this, e);
    }
    #endregion

    #region Init
    protected override void OnInit()
    {
        if (this.skillCollider == null)
            this.skillCollider = this.gameObject.GetComponent<Collider>();
        this.factionEntity = Entity as IFactionEntity;
        this.skillCanvas = gameObjectSkillCanvas.GetComponent<Canvas>();

        cooldown.Init(gameMgr, OnCooldownOver);

        skillCanvas.enabled = false;
        skillIndicatorImage.enabled = false;
        skillCollider.enabled = false;

        if (!Entity.IsFactionEntity())
        {
            Debug.LogError($"[UnitSkill] This component can only be attached to unit or building entities!", gameObject);
            return;
        }

        raycast = new RaycastHitter(m_LayerMask);

        this.taskMgr = gameMgr.GetService<ITaskManager>();
        this.globalEvent = gameMgr.GetService<IGlobalEventPublisher>();
        this.inputMgr = gameMgr.GetService<IInputManager>();
    }
    #endregion

    #region Update

    public void SkillCanvasUpdate(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        switch (skillType)
        {
            case SkillType.Cat:
                SkillCanvasUpdateCat(taskAttributes, target);
                break;
            case SkillType.Dog:
                SkillCanvasUpdateDog(taskAttributes, target);
                break;
        }
    }

    public void SkillCanvasUpdateCat(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        Quaternion rotation = Quaternion.LookRotation(target.position - transform.position);
        rotation.eulerAngles = new Vector3(0, rotation.eulerAngles.y, rotation.eulerAngles.z);

        skillCanvas.transform.rotation = Quaternion.Lerp(rotation, skillCanvas.transform.rotation, 0);
    }

    public void SkillCanvasUpdateDog(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        if (Vector3.Distance(target.position, transform.position) > maxAbilityDistance)
        {
            target.position = transform.position + (target.position - transform.position).normalized * maxAbilityDistance;
        }

        skillCanvas.transform.position = target.position;
    }
    #endregion

    #region Task UI
    protected override bool OnTaskUICacheUpdate(List<EntityComponentTaskUIAttributes> taskUIAttributesCache, List<string> disabledTaskCodesCache)
    {
        if (RTSHelper.OnSingleTaskUIRequest(
                this,
                taskUIAttributesCache,
                disabledTaskCodesCache,
                taskUI,
                requireActiveComponent: false,
                showCondition: IsActive,
                lockedCondition: cooldown.IsActive,
                lockedData: setTargetCooldownUIData) == false)
            return false;

        return true;
    }

    public override bool OnTaskUIClick(EntityComponentTaskUIAttributes taskAttributes)
    {
        if (taskUI.IsValid() && taskAttributes.data.code == taskUI.Key)
        {
            this.gameObjectSkillCanvas.SetActive(true);
            skillCanvas.enabled = true;
            skillIndicatorImage.enabled = true;
            taskMgr.AwaitingTask.Enable(taskAttributes);
            return true;
        }

        return false;
    }


    protected sealed override void OnDisabled()
    {
        this.gameObjectSkillCanvas.SetActive(false);
        skillCanvas.enabled = false;
        skillIndicatorImage.enabled = false;

        Debug.Log("OnDisabled: " + IsDisabled);
        IsDisabled = true;
        if (IsDisabled)
            return;

        IsDisabled = true;
    }

    public override bool OnAwaitingTaskTargetSet(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        if (base.OnAwaitingTaskTargetSet(taskAttributes, target))
            return true;

        else if (taskUI.IsValid() && taskAttributes.data.code == taskUI.Key)
        {
            Launch(taskAttributes, target);

            return true;
        }

        return false;
    }

    public virtual ErrorMessage SetTarget(SetTargetInputData input)
    {
        if (Entity.TasksQueue.IsValid() && Entity.TasksQueue.CanAdd(input))
        {
            return Entity.TasksQueue.Add(new SetTargetInputData
            {
                componentCode = Code,

                target = input.target,
                playerCommand = input.playerCommand,
            });
        }

        return inputMgr.SendInput(
            new CommandInput()
            {
                sourceMode = (byte)InputMode.entity,
                targetMode = (byte)InputMode.setComponentTarget,

                targetPosition = input.target.position,
                opPosition = input.target.opPosition,

                code = Code,
                playerCommand = input.playerCommand,

                intValues = inputMgr.ToIntValues((int)input.BooleansToMask())
            },
            source: factionEntity,
            target: input.target.instance);
    }
    #endregion

    private void Launch(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        OnLaunch();

        switch (skillType)
        {
            case SkillType.Cat:
                LaunchCat(taskAttributes, target);
                break;
            case SkillType.Dog:
                LaunchDog(taskAttributes, target);
                break;
        }

        globalEvent.RaiseEntityComponentTaskUIReloadRequestGlobal(this, new TaskUIReloadEventArgs(reloadAll: false));
    }

    private void LaunchCat(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        Quaternion rotation = Quaternion.LookRotation(target.position - transform.position);
        rotation.eulerAngles = new Vector3(0, rotation.eulerAngles.y, rotation.eulerAngles.z);

        factionEntity.MovementComponent.UpdateRotationTarget(rotation, true);
        // Activate cooldown (if it can be enabled)
        cooldown.IsActive = true;
        if (cooldown.IsActive)
            RaiseCooldownUpdated();

        // Deal Damaged
        StartCoroutine(DealDamage(0.0f ,2.0f, rotation));
    }

    private void LaunchDog(EntityComponentTaskUIAttributes taskAttributes, TargetData<IEntity> target)
    {
        StartCoroutine(DealDamage(0.7f, 2.0f, Quaternion.identity));
        Quaternion rotation = Quaternion.LookRotation(target.position - transform.position);
        rotation.eulerAngles = new Vector3(0, rotation.eulerAngles.y, rotation.eulerAngles.z);
        

        factionEntity.MovementComponent.Stop();
        factionEntity.MovementComponent.UpdateRotationTarget(rotation, true);

        // Activate cooldown (if it can be enabled)
        cooldown.IsActive = true;
        if (cooldown.IsActive)
            RaiseCooldownUpdated();

        factionEntity.AnimatorController.ResetOverrideController();
        factionEntity.AnimatorController.SetOverrideController(skillAnimation);

        if (Vector3.Distance(target.position, transform.position) > maxAbilityDistance)
        {
            target.position = transform.position + (target.position - transform.position).normalized * maxAbilityDistance;
        }

        factionEntity.MovementComponent.SetTarget(target, 0.2f, new MovementSource
        {
            playerCommand = true,
            isMoveAttackRequest = false,
            inMoveAttackChain = false,
            isMoveAttackSource = false,
            fromTasksQueue = false,
            disableMarker = false,
            sourceTargetComponent = null,
            testTargetInRange = false,
            targetAddableUnit = null,
            targetAddableUnitPosition = Vector3.zero
        });
        factionEntity.AnimatorController.ResetAnimatorOverrideControllerOnIdle();
    }


    IEnumerator DealDamage(float timeToWait,float time, Quaternion rotation)
    {
        if (skillSound != null)
        {
            AudioSource.PlayClipAtPoint(skillSound, transform.position, 0.5f);
        }
        if (skillEffect != null)
        {
            yield return new WaitForSeconds(timeToWait);
            GameObject effectObject = Instantiate(skillEffect, transform.position, rotation);
            Destroy(effectObject, time);
            effectObject.SetActive(true);
        }
        skillCollider.enabled = true;
        yield return new WaitForSeconds(1.0f);
        skillCollider.enabled = false;
    }

    protected virtual void OnLaunch() { }

    private void OnTriggerEnter(Collider other)
    {
        if(other.GetComponent<EntitySelectionCollider>() == null) return;
        EntitySelectionCollider entitySelectionCollider = other.GetComponent<EntitySelectionCollider>();
        IFactionEntity hitEntity = entitySelectionCollider.Entity as IFactionEntity;
        Debug.Log(hitEntity.Name);
        if (hitEntity == null) return;
        if (hitEntity.FactionID != factionEntity.FactionID)
        {
            Debug.Log("Hit Enemy: " + hitEntity.Name);
            int damage = DamageData.Get(hitEntity);
            hitEntity.Health.Add(new HealthUpdateArgs(-damage, hitEntity));
            RaiseAttackDamageDealt(new HealthUpdateArgs(damage, hitEntity));
        }
    }
}
