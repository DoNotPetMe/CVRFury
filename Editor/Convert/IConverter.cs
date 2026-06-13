namespace CVRFury.Builder.Convert
{
    /// <summary>One step of the VRChat→ChilloutVR conversion. Steps run in ascending
    /// <see cref="Order"/>; each decides via <see cref="ShouldRun"/> whether the options enable it.</summary>
    internal interface IConverter
    {
        string Title { get; }
        int Order { get; }
        bool ShouldRun(ConversionContext ctx);
        void Run(ConversionContext ctx);
    }
}
