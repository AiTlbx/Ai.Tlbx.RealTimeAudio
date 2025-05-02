using Ai.Tlbx.RealTimeAudio.OpenAi;
using Microsoft.AspNetCore.Components;

namespace Ai.Tlbx.RealTime.WebUi.Services
{
    /// <summary>
    /// Interface for accessing the OpenAiRealTimeApiAccess service in components.
    /// To use this in a component, inject the CascadingParameter:
    /// [CascadingParameter] private OpenAiRealTimeApiAccess RealTimeApiAccess { get; set; }
    /// </summary>
    public static class OpenAiAccessHelper
    {
        /// <summary>
        /// Render fragment that provides the OpenAiRealTimeApiAccess as a cascading value.
        /// Use in your layout or page like:
        /// <code>
        /// @inject OpenAiRealTimeApiAccess rta
        /// <CascadingValue Value="rta">
        ///     @Body
        /// </CascadingValue>
        /// </code>
        /// </summary>
        public static RenderFragment ProvideCascadingApiAccess(OpenAiRealTimeApiAccess access, RenderFragment childContent) =>
            builder =>
            {
                builder.OpenComponent<CascadingValue<OpenAiRealTimeApiAccess>>(0);
                builder.AddAttribute(1, "Value", access);
                builder.AddAttribute(2, "ChildContent", childContent);
                builder.CloseComponent();
            };
    }
} 