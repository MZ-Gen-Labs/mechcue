namespace MechCue;
public partial class MainForm
{
    void ShowMotionContacts(IWin32Window owner)
    {
        var parts=System.Text.Json.JsonSerializer.SerializeToElement(bridge.ListMotionInspectionParts()).GetProperty("parts").EnumerateArray().Select(p=>new ContactPart(p.GetProperty("Name").GetString()!,p.GetProperty("KeyPath").GetString()!)).ToArray();
        var entries=bridge.InspectionPolicy.AllowedContacts.ToList();
        using var dialog=new Form{Text="意図した接触の組",Width=820,Height=470,StartPosition=FormStartPosition.CenterParent};
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=115};
        var first=new ComboBox{Width=350,DropDownStyle=ComboBoxStyle.DropDownList,DataSource=parts.ToArray()};
        var second=new ComboBox{Width=350,DropDownStyle=ComboBoxStyle.DropDownList,DataSource=parts.ToArray()};
        if(parts.Length>1)second.SelectedIndex=1;
        var reason=new TextBox{Width=510,MaxLength=500,PlaceholderText="接触を認める理由（必須）"};
        var add=new Button{Text="この組を追加",AutoSize=true};top.Controls.AddRange([first,second,reason,add]);
        var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=70};
        var remove=new Button{Text="選択した組を削除",AutoSize=true};var save=new Button{Text="設定を適用",AutoSize=true};
        bottom.Controls.AddRange([new Label{Text="指定した組の干渉・すきま判定をすべて除外します。別の組は引き続き検査します。",AutoSize=true},remove,save]);
        string Label(AllowedMotionContact contact)=>$"{parts.FirstOrDefault(p=>p.Key==contact.FirstKeyPath)?.Name??"参照不明"} ↔ {parts.FirstOrDefault(p=>p.Key==contact.SecondKeyPath)?.Name??"参照不明"} / {contact.Reason}";
        void RefreshList(){list.Items.Clear();foreach(var entry in entries)list.Items.Add(Label(entry));}
        add.Click+=(_,_)=>{
            try{if(first.SelectedItem is not ContactPart a||second.SelectedItem is not ContactPart b)throw new InvalidOperationException("部品を2つ選んでください。");var proposal=bridge.InspectionPolicy with{AllowedContacts=[..entries,new(a.Key,b.Key,reason.Text.Trim())]};bridge.ValidateMotionInspectionPolicy(proposal);entries=proposal.AllowedContacts.ToList();RefreshList();}
            catch(Exception error){MessageBox.Show(dialog,(error.InnerException??error).Message,"接触設定");}
        };
        remove.Click+=(_,_)=>{if(list.SelectedIndex>=0){entries.RemoveAt(list.SelectedIndex);RefreshList();}};
        save.Click+=(_,_)=>{try{var policy=bridge.InspectionPolicy with{AllowedContacts=entries.ToArray()};bridge.ValidateMotionInspectionPolicy(policy);bridge.InspectionPolicy=policy;lastCheckedTime=null;MarkDocumentSettingsChanged();dialog.DialogResult=DialogResult.OK;dialog.Close();}catch(Exception error){MessageBox.Show(dialog,(error.InnerException??error).Message,"接触設定");}};
        dialog.Controls.Add(list);dialog.Controls.Add(top);dialog.Controls.Add(bottom);RefreshList();dialog.ShowDialog(owner);
    }
    sealed record ContactPart(string Name,string Key){public override string ToString()=>Name;}
}
