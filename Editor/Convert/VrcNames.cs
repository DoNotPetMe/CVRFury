namespace CVRFury.Builder.Convert
{
    /// <summary>
    /// Every VRChat SDK and DynamicBone type/member name the converter touches by reflection, in
    /// one place (mirroring <c>CckNames</c>). The converter reads VRChat avatar data without a
    /// compile-time dependency on the VRChat SDK; it only works when the SDK is present in the
    /// project (so the types are loadable). If a future SDK renames a member, update it here — the
    /// converter logs a clear diagnostic and skips, rather than corrupting the avatar.
    /// </summary>
    internal static class VrcNames
    {
        // --- Avatar descriptor ---
        public const string AvatarDescriptorType = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        public const string Desc_ViewPosition = "ViewPosition";
        public const string Desc_LipSync = "lipSync";                  // enum VisemeBlendShape / JawFlapBlendShape / ...
        public const string Desc_VisemeMesh = "VisemeSkinnedMesh";     // SkinnedMeshRenderer
        public const string Desc_VisemeBlendShapes = "VisemeBlendShapes"; // string[15]
        public const string Desc_EyeLook = "customEyeLookSettings";    // struct CustomEyeLookSettings
        public const string Desc_ExpressionsMenu = "expressionsMenu";  // VRCExpressionsMenu
        public const string Desc_ExpressionParameters = "expressionParameters"; // VRCExpressionParameters
        public const string Desc_BaseAnimationLayers = "baseAnimationLayers";   // CustomAnimLayer[]

        // CustomEyeLookSettings struct
        public const string Eye_EyelidsMesh = "eyelidsSkinnedMesh";
        public const string Eye_EyelidsBlendshapes = "eyelidsBlendshapes"; // int[] (blink, lookUp, lookDown)
        public const string Eye_EyelidType = "eyelidType";

        // CustomAnimLayer
        public const string Layer_Type = "type";                       // enum: Base/Additive/Gesture/Action/FX/...
        public const string Layer_Controller = "animatorController";   // RuntimeAnimatorController
        public const string Layer_IsDefault = "isDefault";

        // --- Expression parameters ---
        public const string ExpressionParametersType = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters";
        public const string ExprParams_List = "parameters";            // Parameter[]
        public const string ExprParam_Name = "name";
        public const string ExprParam_ValueType = "valueType";         // enum Int/Float/Bool
        public const string ExprParam_DefaultValue = "defaultValue";   // float
        public const string ExprParam_Saved = "saved";
        public const string ExprParam_NetworkSynced = "networkSynced";

        // --- Expressions menu ---
        public const string ExpressionsMenuType = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu";
        public const string Menu_Controls = "controls";                // List<Control>
        public const string Control_Name = "name";
        public const string Control_Type = "type";                     // enum Button/Toggle/SubMenu/TwoAxisPuppet/FourAxisPuppet/RadialPuppet
        public const string Control_Parameter = "parameter";           // struct { name }
        public const string Control_ParameterName = "name";
        public const string Control_Value = "value";                   // float
        public const string Control_SubMenu = "subMenu";               // VRCExpressionsMenu
        public const string Control_SubParameters = "subParameters";   // Parameter[]

        // --- PhysBones ---
        public const string PhysBoneType = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
        public const string PB_Root = "rootTransform";
        public const string PB_IgnoreTransforms = "ignoreTransforms";
        public const string PB_EndpointPosition = "endpointPosition";
        public const string PB_Radius = "radius";
        public const string PB_Pull = "pull";
        public const string PB_Spring = "spring";
        public const string PB_Stiffness = "stiffness";
        public const string PB_Gravity = "gravity";
        public const string PB_Immobile = "immobile";
        public const string PB_Colliders = "colliders";               // List<VRCPhysBoneColliderBase>

        public const string PhysBoneColliderType = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider";
        public const string PBC_ShapeType = "shapeType";               // enum Sphere/Capsule/Plane
        public const string PBC_Radius = "radius";
        public const string PBC_Height = "height";
        public const string PBC_Position = "position";
        public const string PBC_RootTransform = "rootTransform";

        // VRChat contacts (no clean CVR equivalent — flagged for stripping).
        public const string ContactReceiverType = "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver";
        public const string ContactSenderType = "VRC.SDK3.Dynamics.Contact.Components.VRCContactSender";

        // --- DynamicBone (the classic plugin ChilloutVR uses) ---
        public const string DynamicBoneType = "DynamicBone";
        public const string DB_Root = "m_Root";
        public const string DB_Damping = "m_Damping";
        public const string DB_Elasticity = "m_Elasticity";
        public const string DB_Stiffness = "m_Stiffness";
        public const string DB_Inert = "m_Inert";
        public const string DB_Radius = "m_Radius";
        public const string DB_EndLength = "m_EndLength";
        public const string DB_EndOffset = "m_EndOffset";
        public const string DB_Gravity = "m_Gravity";
        public const string DB_Force = "m_Force";
        public const string DB_Colliders = "m_Colliders";
        public const string DB_Exclusions = "m_Exclusions";

        public const string DynamicBoneColliderType = "DynamicBoneCollider";
        public const string DBC_Radius = "m_Radius";
        public const string DBC_Height = "m_Height";
        public const string DBC_Center = "m_Center";
        public const string DBC_Direction = "m_Direction";
        public const string DBC_Bound = "m_Bound";

        // --- Worlds (VRChat Worlds SDK) ---
        // Field names vary a little across SDK generations, so the world converter tries each
        // candidate in order and copies the first that exists (see WorldConverter.CopyAny).
        public const string SceneDescriptorType = "VRC.SDK3.Components.VRCSceneDescriptor";
        public static readonly string[] World_Spawns = { "spawns", "Spawns" };
        public static readonly string[] World_ReferenceCamera = { "ReferenceCamera", "referenceCamera" };
        public static readonly string[] World_RespawnHeight = { "RespawnHeightY", "respawnHeightY" };

        public const string MirrorType = "VRC.SDK3.Components.VRCMirrorReflection";
        public const string PickupType = "VRC.SDK3.Components.VRCPickup";
        public const string StationType = "VRC.SDK3.Components.VRCStation";
        public const string AvatarPedestalType = "VRC.SDK3.Components.VRCAvatarPedestal";
        public const string UdonBehaviourType = "VRC.Udon.UdonBehaviour";
        public const string Udon_ProgramSource = "programSource";
    }
}
