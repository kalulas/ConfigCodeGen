﻿using System;
using System.IO;
using System.Threading.Tasks;
using Mono.TextTemplating;
using TableCraft.Core.ConfigReader;
using TableCraft.Core.Generation;
using TableCraft.Core.IO;

namespace TableCraft.Core
{
    /// <summary>
    /// ConfigManager(singleton)
    /// </summary>
    public class ConfigManager
    {
        private ConfigManager()
        {
            if (!Configuration.IsInited)
            {
                Debugger.LogError("[ConfigManager] Configuration not setup yet! run Configuration.ReadConfigurationFromJson() first");
            }
        }

        #region Singleton

        private static ConfigManager m_Singleton;

        public static ConfigManager singleton => m_Singleton ??= new ConfigManager();

        #endregion

        #region Public API

        /// <summary>
        /// Create a ConfigInfo instance with DataSource and DataDecorators
        /// </summary>
        /// <param name="dataSourcePath"></param>
        /// <param name="dataDecoratorPaths"></param>
        /// <returns></returns>
        public ConfigInfo CreateConfigInfo(string dataSourcePath, string[] dataDecoratorPaths)
        {
            return ConfigInfoFactory.CreateConfigInfo(dataSourcePath, dataDecoratorPaths);
        }

        /// <summary>
        /// Save all additional information with a new created <see cref="TableCraft.Core.Decorator.IDataDecorator"/>
        /// </summary>
        /// <param name="configInfo"></param>
        /// <param name="decoratorPath"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool SaveConfigInfoWithDecorator(ConfigInfo configInfo, string decoratorPath)
        {
            if (configInfo == null)
            {
                throw new ArgumentNullException(nameof(configInfo), "configInfo is null");
            }

            var decorator = ConfigInfoFactory.CreateDataDecorator(decoratorPath);
            var success = configInfo.SaveWith(decorator);
            return success;
        }

        /// <summary>
        /// Generate code with specific usage to <paramref name="outputDirectory"/>
        /// </summary>
        /// <param name="usage"></param>
        /// <param name="configInfo"></param>
        /// <param name="outputDirectory"></param>
        /// <returns></returns>
        public async Task<bool> GenerateCodeForUsage(string usage, ConfigInfo configInfo, string outputDirectory)
        {
            if (configInfo == null)
            {
                Debugger.LogWarning("[ConfigManager.GenerateCodeForUsage] configInfo is null");
                return false;
            }

            if (!Configuration.IsUsageValid(usage))
            {
                Debugger.LogWarning($"[ConfigManager.GenerateCodeForUsage] usage '{usage}' is not valid");
                return false;
            }
            
            var templateFilePath = Configuration.GetTemplateFilePathForUsage(usage);
            var outputExtension = Configuration.GetTargetFileTypeForUsage(usage);
            if (string.IsNullOrEmpty(templateFilePath) || string.IsNullOrEmpty(outputExtension) || !File.Exists(templateFilePath))
            {
                Debugger.LogWarning("[ConfigManager.GenerateCodeForUsage] template file {1} not found for usage '{0}'",
                    usage, templateFilePath ?? string.Empty);
                return false;
            }

            var outputFileName = Path.ChangeExtension(configInfo.GetExportName(usage), outputExtension);
            var outputFilePath = Path.Combine(outputDirectory, outputFileName ?? string.Empty);
            if (File.Exists(outputFilePath))
            {
                Debugger.LogWarning("[ConfigManager.GenerateCodeForUsage] existed file '{0}' will be overwrite", outputFilePath);
            }

            // read from template file
            var templateContent = await FileHelper.ReadToEnd(templateFilePath);

            var generator = new CustomTemplateGenerator(templateFilePath, configInfo);
            var parsed = generator.ParseTemplate(templateFilePath, templateContent);
            // additional settings available
            var settings = TemplatingEngine.GetSettings(generator, parsed);

            // previous Microsoft.VisualStudio.TextTemplating
            // var host = new CustomTemplateGenerator(templateFilePath, configInfo); // var engine = new Engine();
            // transform the text template
            // var outputContent = engine.ProcessTemplate(templateContent, host);
            
            Debugger.Log($"[ConfigManager.GenerateCodeForUsage] start processing template file {templateFilePath}");
            
            var (finalOutputFilePath, generatedContent) =
                await generator.ProcessTemplateAsync(parsed, templateFilePath, templateContent, outputFilePath,
                    settings);

            Debugger.Log(
                $"[ConfigManager.GenerateCodeForUsage] finish processing template file {templateFilePath}");
            // make sure target output directory existed
            // add a extra try-catch for more information
            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception e)
            {
                Debugger.LogError($"Exception {e} was thrown when creating directory {outputDirectory}");
                throw;
            }
            
            await FileHelper.WriteAsync(finalOutputFilePath, generatedContent);
            Debugger.Log(
                $"[ConfigManager.GenerateCodeForUsage] finish writing generated content to file {finalOutputFilePath}");
            // generation errors to logger
            var success = generator.PrintErrors();
            return success;
        }

        #endregion
    }
}
