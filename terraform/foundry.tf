# resource "azurerm_cognitive_account" "foundry" {
#   name                = "rsd-foundry"
#   location            = azurerm_resource_group.default.location
#   resource_group_name = azurerm_resource_group.default.name
#   kind                = "AIServices"

#   sku_name = "F0"

#   public_network_access_enabled = "false"

#   network_acls {
#     default_action = "Deny"
#     ip_rules       = local.foundry_ipv4_allow_list # Use global protect ACL
#   }

#   tags = {
#     # Acceptance = "Test"
#   }
# }