﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using AutoMapper;
using umbraco;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Mapping;
using Umbraco.Core.PropertyEditors;
using Umbraco.Core.Services;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Web.Trees;

namespace Umbraco.Web.Models.Mapping
{
    /// <summary>
    /// Declares model mappings for media.
    /// </summary>
    internal class MediaModelMapper : MapperConfiguration
    {
        public override void ConfigureMappings(IConfiguration config, ApplicationContext applicationContext)
        {
            //FROM IMedia TO MediaItemDisplay
            config.CreateMap<IMedia, MediaItemDisplay>()
                .ForMember(
                    dto => dto.Owner,
                    expression => expression.ResolveUsing<OwnerResolver<IMedia>>())
                .ForMember(
                    dto => dto.Icon,
                    expression => expression.MapFrom(content => content.ContentType.Icon))
                .ForMember(
                    dto => dto.ContentTypeAlias,
                    expression => expression.MapFrom(content => content.ContentType.Alias))
                .ForMember(display => display.IsChildOfListView, expression => expression.Ignore())
                .ForMember(
                    dto => dto.Trashed,
                    expression => expression.MapFrom(content => content.Trashed))
                .ForMember(
                    dto => dto.ContentTypeName,
                    expression => expression.MapFrom(content => content.ContentType.Name))
                .ForMember(display => display.Properties, expression => expression.Ignore())
                .ForMember(display => display.TreeNodeUrl, expression => expression.Ignore())
                .ForMember(display => display.Notifications, expression => expression.Ignore())
                .ForMember(display => display.Errors, expression => expression.Ignore())
                .ForMember(display => display.Published, expression => expression.Ignore())
                .ForMember(display => display.Updater, expression => expression.Ignore())
                .ForMember(display => display.Alias, expression => expression.Ignore())
                .ForMember(display => display.IsContainer, expression => expression.Ignore())
                .ForMember(display => display.Tabs, expression => expression.ResolveUsing(new TabsAndPropertiesResolver(applicationContext.Services.TextService)))
                .AfterMap((media, display) => AfterMap(media, display, applicationContext.Services.DataTypeService, applicationContext.Services.TextService));

            //FROM IMedia TO ContentItemBasic<ContentPropertyBasic, IMedia>
            config.CreateMap<IMedia, ContentItemBasic<ContentPropertyBasic, IMedia>>()
                .ForMember(
                    dto => dto.Owner,
                    expression => expression.ResolveUsing<OwnerResolver<IMedia>>())
                .ForMember(
                    dto => dto.Icon,
                    expression => expression.MapFrom(content => content.ContentType.Icon))
                .ForMember(
                    dto => dto.Trashed,
                    expression => expression.MapFrom(content => content.Trashed))
                .ForMember(
                    dto => dto.ContentTypeAlias,
                    expression => expression.MapFrom(content => content.ContentType.Alias))
                .ForMember(x => x.Published, expression => expression.Ignore())
                .ForMember(x => x.Updater, expression => expression.Ignore())
                .ForMember(x => x.Alias, expression => expression.Ignore());

            //FROM IMedia TO ContentItemDto<IMedia>
            config.CreateMap<IMedia, ContentItemDto<IMedia>>()
                .ForMember(
                    dto => dto.Owner,
                    expression => expression.ResolveUsing<OwnerResolver<IMedia>>())
                .ForMember(x => x.Published, expression => expression.Ignore())
                .ForMember(x => x.Updater, expression => expression.Ignore())
                .ForMember(x => x.Icon, expression => expression.Ignore())
                .ForMember(x => x.Alias, expression => expression.Ignore());
        }

        private static void AfterMap(IMedia media, MediaItemDisplay display, IDataTypeService dataTypeService, ILocalizedTextService localizedText)
        {
			// Adapted from ContentModelMapper
			//map the IsChildOfListView (this is actually if it is a descendant of a list view!)
            //TODO: Fix this shorthand .Ancestors() lookup, at least have an overload to use the current
            if (media.HasIdentity)
            {
                var ancesctorListView = media.Ancestors().FirstOrDefault(x => x.ContentType.IsContainer);
                display.IsChildOfListView = ancesctorListView != null;
            }
            else
            {
                //it's new so it doesn't have a path, so we need to look this up by it's parent + ancestors
                var parent = media.Parent();
                if (parent == null)
                {
                    display.IsChildOfListView = false;
                }
                else if (parent.ContentType.IsContainer)
                {
                    display.IsChildOfListView = true;
                }
                else
                {
                    var ancesctorListView = parent.Ancestors().FirstOrDefault(x => x.ContentType.IsContainer);
                    display.IsChildOfListView = ancesctorListView != null;
                }
            }
			
            //map the tree node url
            if (HttpContext.Current != null)
            {
                var urlHelper = new UrlHelper(new RequestContext(new HttpContextWrapper(HttpContext.Current), new RouteData()));
                var url = urlHelper.GetUmbracoApiService<MediaTreeController>(controller => controller.GetTreeNode(display.Id.ToString(), null));
                display.TreeNodeUrl = url;
            }
            
            if (media.ContentType.IsContainer)
            {
                TabsAndPropertiesResolver.AddListView(display, "media", dataTypeService, localizedText);
            }
            
            var genericProperties = new List<ContentPropertyDisplay>
            {
                new ContentPropertyDisplay
                {
                    Alias = string.Format("{0}doctype", Constants.PropertyEditors.InternalGenericPropertiesPrefix),
                    Label = localizedText.Localize("content/mediatype"),
                    Value = localizedText.UmbracoDictionaryTranslate(display.ContentTypeName),
                    View = PropertyEditorResolver.Current.GetByAlias(Constants.PropertyEditors.NoEditAlias).ValueEditor.View
                }
            };

            if (media.Properties.FirstOrDefault(x => x.Alias == Constants.Conventions.Media.File) != null)
            {
                var helper = new UmbracoHelper(UmbracoContext.Current);
                var mediaItem = helper.TypedMedia(media.Id);
                if (mediaItem != null)
                {
                    var crops = new List<string>();
                    var autoFillProperties = UmbracoConfig.For.UmbracoSettings().Content.ImageAutoFillProperties;
                    foreach (var field in autoFillProperties)
                    {
                        var crop = mediaItem.GetCropUrl(field.Alias, string.Empty);
                        if (string.IsNullOrWhiteSpace(crop) == false)
                            crops.Add(crop.Split('?')[0]);
                    }
                    
                    if (crops.Any())
                    {
                        var link = new ContentPropertyDisplay
                        {
                            Alias = string.Format("{0}urls", Constants.PropertyEditors.InternalGenericPropertiesPrefix),
                            Label = localizedText.Localize("media/urls"),
                            // don't add the querystring, split on the "?" will also work if there is no "?"
                            Value = string.Join(",", crops),
                            View = "urllist"
                        };

                        genericProperties.Add(link);
                    }
                }
            }

            TabsAndPropertiesResolver.MapGenericProperties(media, display, localizedText, genericProperties);
        }

    }
}
