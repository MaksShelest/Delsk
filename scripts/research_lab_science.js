/* Мастер научных экспериментов. Числа считает сервер, здесь только формы и чтение отчёта.
   Author: Шелестов Максим Евгеньевич. */
'use strict';
/* esc, bytes и labels уже объявлены в research_lab.js. Повторный const в этом же окне ломает всю страницу. */
const pageOf=window.labPage,stateOf=window.labState,catalogOf=window.labCatalog;
let scienceCatalog=null,scienceView='list',scienceStep=1,scienceId=null,scienceTab='overview',sciencePayload=null,scienceStamp='',scienceDraft=null,scienceChart=null;
const scienceRoot=()=>document.getElementById('science-root');
const scienceTemplates=()=>scienceCatalog?.templates??[];
const scienceTemplate=()=>scienceTemplates().find(item=>item.id===scienceDraft.template);
const scienceMetric=(id)=>scienceCatalog.metrics.find(item=>item.id===id);
function scienceCoins(minor){if(minor==null||minor==='')return '—';const n=Number(minor)/100;return n.toLocaleString('ru-RU',{minimumFractionDigits:2,maximumFractionDigits:2})+' монеты';}
function scienceNumber(value,metric){if(value==null||value==='')return '—';if(metric?.money)return scienceCoins(value);if(metric?.unitLabel==='секунды')return (Number(value)/1000).toLocaleString('ru-RU',{maximumFractionDigits:1})+' с';const n=Number(value);return Number.isFinite(n)?n.toLocaleString('ru-RU',{maximumFractionDigits:4}):String(value);}
function scienceStatus(job){const retry=job&&['failed','timeout','interrupted','cancelled'].includes(job.status);return (labels[job?.status]||job?.status||'—')+(retry?' · требует повторного запуска':'');}
function applyTemplate(id){
 const template=scienceTemplates().find(item=>item.id===id);
 scienceDraft.template=id;scienceDraft.title=template.title;scienceDraft.goal=template.goal;scienceDraft.hypothesis=template.hypothesis;scienceDraft.comparison=template.comparison;scienceDraft.primary=template.primary;
 scienceDraft.metrics=[template.primary,...(template.extras||[])].filter((item,index,all)=>item&&all.indexOf(item)===index);
 if(template.values)scienceDraft.groups=template.values.map((value,index)=>({label:String.fromCharCode(65+index),value}));
 else{const factor=scienceCatalog.customFactors[0];scienceDraft.factor=factor.path;scienceDraft.groups=factor.examples.slice(0,2).map((value,index)=>({label:String.fromCharCode(65+index),value}));}
}
async function scienceShow(){if(!scienceCatalog)scienceCatalog=await api('/api/science/catalog');if(!scienceDraft){scienceDraft={};applyTemplate('wages');scienceDraft.ticks=120;scienceDraft.seedCount=4;scienceDraft.firstSeed=42;scienceDraft.repetitions=1;scienceDraft.recording='normal';scienceDraft.npcs=3;}renderScience();}
function renderScience(){if(scienceView==='wizard')renderWizard();else if(scienceView==='study')renderStudy();else renderList();}
window.scienceShow=scienceShow;
window.scienceTick=async function(){if(pageOf()!=='science'||scienceView==='wizard')return;if(scienceView==='list'){renderList();return;}if(!scienceId)return;const data=await api('/api/science/study?id='+encodeURIComponent(scienceId));const stamp=data.series.jobs.map(job=>[job.status,job.attempt,job.progress?.tick,job.progress?.percent].join(':')).join('|')+'|'+(data.study?.fingerprint||'');if(stamp===scienceStamp)return;scienceStamp=stamp;sciencePayload=data;renderStudy();};
function renderList(){
 const rows=(stateOf().series||[]).filter(item=>item.plan?.science);
 scienceRoot().innerHTML='<div class="page-title"><div><span class="eyebrow">НАУЧНЫЕ ЭКСПЕРИМЕНТЫ</span><h1>Сравнить условия</h1><p>Гипотеза, две или больше групп, один изменяемый параметр и автоматический отчёт. Игровые правила не меняются.</p></div><button class="primary" id="science-new" type="button">Создать исследование</button></div>'+(rows.length?('<div class="cards">'+rows.map(item=>{const done=item.jobs.filter(job=>job.status==='succeeded').length;return '<article class="card"><span class="pill">'+esc(item.plan.science.template)+'</span><h2><button type="button" data-science="'+esc(item.id)+'">'+esc(item.name||item.plan.title)+'</button></h2><p>'+esc(item.plan.science.hypothesis)+'</p><p>'+done+' / '+item.jobs.length+' успешно</p></article>';}).join('')+'</div>'):'<div class="empty"><h2>Пока нет научных серий</h2><p class="muted">Начните с шаблона «Влияние зарплат на экономику».</p></div>');
 document.getElementById('science-new').onclick=()=>{scienceView='wizard';scienceStep=1;renderWizard();};
 scienceRoot().querySelectorAll('[data-science]').forEach(button=>button.onclick=()=>openScience(button.dataset.science));
}
function groupEditor(group,index){
 const template=scienceTemplate();
 const kind=template.id==='custom'?(scienceCatalog.customFactors.find(item=>item.path===scienceDraft.factor)?.kind):template.kind;
 const value=group.value;
 if(kind==='text')return '<select data-group="'+index+'">'+['Easy','Medium','Hard'].map(item=>'<option '+(item===value?'selected':'')+'>'+item+'</option>').join('')+'</select>';
 if(kind==='bool')return '<select data-group="'+index+'"><option value="true" '+(value===true?'selected':'')+'>Включены</option><option value="false" '+(value===false?'selected':'')+'>Выключены</option></select>';
 if(kind==='count')return '<input data-group="'+index+'" type="number" min="3" max="10" value="'+esc(value)+'">';
 return '<input data-group="'+index+'" type="number" min="0" step="0.01" value="'+esc((Number(value)/100).toFixed(2))+'"> <span class="muted">монеты</span>';
}
function readGroups(){
 const template=scienceTemplate();
 const kind=template.id==='custom'?(scienceCatalog.customFactors.find(item=>item.path===scienceDraft.factor)?.kind):template.kind;
 scienceRoot().querySelectorAll('[data-group]').forEach(input=>{
  const group=scienceDraft.groups[Number(input.dataset.group)];
  if(kind==='bool')group.value=input.value==='true';
  else if(kind==='text')group.value=input.value;
  else if(kind==='count')group.value=integer(input.value,3,10,'Количество NPC');
  else group.value=integer(String(Math.round(Number(input.value)*100)),0,1000000000,'Деньги');
 });
}
function renderWizard(){
 const template=scienceTemplate();
 const steps=['Вопрос','Условия','Повторы','Показатели'];
 const mapChoices=template.ordinary?catalogOf().city.maps:catalogOf().maps.filter(item=>String(item.path).includes('presentation-map'));
 if(!scienceDraft.map&&mapChoices[0])scienceDraft.map=mapChoices[0].path;
 const maps=mapChoices.map(item=>'<option value="'+esc(item.path)+'" '+(scienceDraft.map===item.path?'selected':'')+'>'+esc(item.label)+'</option>');
 scienceRoot().innerHTML='<div class="page-title"><div><span class="eyebrow">ШАГ '+scienceStep+' ИЗ 4</span><h1>'+esc(steps[scienceStep-1])+'</h1><p>'+esc(template.fixed||'')+'</p></div><button type="button" id="science-back-list">К списку</button></div><div class="science-steps">'+steps.map((item,index)=>'<span class="'+(index+1===scienceStep?'now':'')+'">'+esc(item)+'</span>').join('')+'</div><div class="panel" id="science-step"></div><div class="button-row"><button type="button" id="science-prev"'+(scienceStep===1?' disabled':'')+'>Назад</button><button class="primary" type="button" id="science-next">'+(scienceStep===4?'Запустить серию':'Дальше')+'</button></div>';
 const host=document.getElementById('science-step');
 if(scienceStep===1)host.innerHTML='<div class="template-grid">'+scienceTemplates().map(item=>'<button type="button" data-template="'+esc(item.id)+'" class="'+(item.id===scienceDraft.template?'active':'')+'"><strong>'+esc(item.title)+'</strong><br><span class="muted">'+esc(item.comparison)+'</span></button>').join('')+'</div><div class="form-grid"><label>Название<input id="science-title" maxlength="80" value="'+esc(scienceDraft.title)+'"></label><label>Основной показатель<select id="science-primary">'+scienceCatalog.metrics.map(item=>'<option value="'+esc(item.id)+'" '+(item.id===scienceDraft.primary?'selected':'')+'>'+esc(item.title)+'</option>').join('')+'</select></label><label>Цель<textarea id="science-goal" rows="3">'+esc(scienceDraft.goal)+'</textarea></label><label>Гипотеза<textarea id="science-hypothesis" rows="3">'+esc(scienceDraft.hypothesis)+'</textarea></label></div><label>Что сравнивается<textarea id="science-comparison" rows="2">'+esc(scienceDraft.comparison)+'</textarea></label>'+(template.id==='custom'?'<label>Параметр<select id="science-factor">'+scienceCatalog.customFactors.map(item=>'<option value="'+esc(item.path)+'" '+(item.path===scienceDraft.factor?'selected':'')+'>'+esc(item.label)+'</option>').join('')+'</select></label>':'');
 if(scienceStep===2)host.innerHTML='<p>Остальные настройки у групп одинаковые. В этом выпуске меняется один параметр.</p>'+scienceDraft.groups.map((group,index)=>'<div class="form-grid"><label>Группа '+esc(group.label)+groupEditor(group,index)+'</label></div>').join('')+'<button type="button" id="science-add-group">Добавить группу</button>';
 if(scienceStep===3)host.innerHTML='<div class="form-grid"><label>Карта<select id="science-map">'+maps.join('')+'</select></label>'+(template.ordinary&&template.factor!=='ordinary.npcCount'?'<label>Алгоритмических NPC<input id="science-npcs" type="number" min="3" max="10" value="'+esc(scienceDraft.npcs)+'"></label>':'')+'<label>Тиков в одном прогоне<input id="science-ticks" type="number" min="1" value="'+esc(scienceDraft.ticks)+'"></label><label>Независимых seeds<input id="science-seeds" type="number" min="1" max="500" value="'+esc(scienceDraft.seedCount)+'"></label><label>Первый seed<input id="science-first" type="number" min="0" value="'+esc(scienceDraft.firstSeed)+'"></label><label>Повторов одного seed<input id="science-repetitions" type="number" min="1" max="20" value="'+esc(scienceDraft.repetitions)+'"></label><label>Запись<select id="science-recording"><option value="brief">Краткая</option><option value="normal">Обычная — срезы для графиков</option><option value="detailed">Подробная — ещё и кадры мира</option></select></label></div><div class="chips"><button type="button" data-chip="seeds">10 seeds</button><button type="button" data-chip="ticks">1000 тиков</button></div><div class="callout" id="science-estimate">Считаем объём…</div><p class="muted">Повтор одного seed не станет вторым независимым наблюдением. Тики внутри прогона тоже не считаются отдельными экспериментами.</p>';
 if(scienceStep===4)host.innerHTML='<p>Основной показатель проверяет гипотезу. Остальные отмеченные — разведочные: их интервалы не поправляются на число сравнений.</p><div class="check-grid">'+scienceCatalog.metrics.map(item=>'<label><input type="checkbox" data-metric="'+esc(item.id)+'" '+(scienceDraft.metrics.includes(item.id)?'checked':'')+(item.id===scienceDraft.primary?' disabled':'')+'>'+esc(item.title)+'<br><span class="muted">'+esc(item.definition)+'</span></label>').join('')+'</div><div class="callout" id="science-review"></div>';
 document.getElementById('science-back-list').onclick=()=>{scienceView='list';renderList();};
 document.getElementById('science-prev').onclick=()=>{captureStep();scienceStep=Math.max(1,scienceStep-1);renderWizard();};
 document.getElementById('science-next').onclick=()=>advance().catch(alertError);
 host.querySelectorAll('[data-template]').forEach(button=>button.onclick=()=>{captureStep();applyTemplate(button.dataset.template);renderWizard();});
 const add=document.getElementById('science-add-group');if(add)add.onclick=()=>{readGroups();if(scienceDraft.groups.length>=6)return;scienceDraft.groups.push({label:String.fromCharCode(65+scienceDraft.groups.length),value:scienceDraft.groups.at(-1).value});renderWizard();};
 host.querySelectorAll('[data-chip]').forEach(button=>button.onclick=()=>{if(button.dataset.chip==='seeds')document.getElementById('science-seeds').value=10;else document.getElementById('science-ticks').value=1000;previewScience().catch(alertError);});
 const recording=document.getElementById('science-recording');if(recording){recording.value=scienceDraft.recording;['science-map','science-ticks','science-seeds','science-first','science-repetitions','science-recording','science-npcs'].forEach(id=>{const node=document.getElementById(id);if(node)node.onchange=()=>previewScience().catch(alertError);});previewScience().catch(alertError);}
 if(scienceStep===4)reviewScience().catch(alertError);
}
function captureStep(){
 if(scienceStep===1){scienceDraft.title=document.getElementById('science-title').value;scienceDraft.goal=document.getElementById('science-goal').value;scienceDraft.hypothesis=document.getElementById('science-hypothesis').value;scienceDraft.comparison=document.getElementById('science-comparison').value;scienceDraft.primary=document.getElementById('science-primary').value;if(!scienceDraft.metrics.includes(scienceDraft.primary))scienceDraft.metrics.unshift(scienceDraft.primary);const factor=document.getElementById('science-factor');if(factor){scienceDraft.factor=factor.value;const chosen=scienceCatalog.customFactors.find(item=>item.path===factor.value);if(chosen&&scienceDraft.groups.some(group=>typeof group.value!==typeof chosen.examples[0]))scienceDraft.groups=chosen.examples.slice(0,2).map((value,index)=>({label:String.fromCharCode(65+index),value}));}}
 if(scienceStep===2)readGroups();
 if(scienceStep===3){const map=document.getElementById('science-map');if(map)scienceDraft.map=map.value;const npcs=document.getElementById('science-npcs');if(npcs)scienceDraft.npcs=integer(npcs.value,3,10,'Количество NPC');scienceDraft.ticks=integer(document.getElementById('science-ticks').value,1,1000000,'Тики');scienceDraft.seedCount=integer(document.getElementById('science-seeds').value,1,500,'Seeds');scienceDraft.firstSeed=integer(document.getElementById('science-first').value,0,Number.MAX_SAFE_INTEGER,'Первый seed');scienceDraft.repetitions=integer(document.getElementById('science-repetitions').value,1,20,'Повторы');scienceDraft.recording=document.getElementById('science-recording').value;}
 if(scienceStep===4)scienceDraft.metrics=[scienceDraft.primary,...[...scienceRoot().querySelectorAll('[data-metric]:checked')].map(item=>item.dataset.metric)].filter((item,index,all)=>all.indexOf(item)===index);
}
function requestBody(){const body={...scienceDraft,groups:scienceDraft.groups.map(group=>({label:group.label,value:group.value}))};if(scienceDraft.template==='custom')body.factor=scienceDraft.factor;return body;}
async function previewScience(){captureStep();const preview=await api('/api/science/preview',requestBody());const host=document.getElementById('science-estimate')||document.getElementById('science-review');if(!host)return preview;const estimate=preview.estimate;host.innerHTML='<strong>'+estimate.runs+' прогонов</strong> · '+estimate.independentSeeds+' независимых seeds. Оценка времени около '+estimate.seconds+' с, журнал решений около '+bytes(estimate.decisionBytes)+'. '+esc(estimate.note);return preview;}
async function reviewScience(){const preview=await previewScience();const plan=preview.plan.science;const host=document.getElementById('science-review');host.innerHTML='<p>'+esc(plan.title)+'</p><ul>'+plan.groups.map(group=>'<li>Группа '+esc(group.label)+': '+esc(group.factorLabel)+' = '+esc(group.valueLabel)+'</li>').join('')+'</ul><ul>'+plan.fixed.map(item=>'<li>'+esc(item)+'</li>').join('')+'</ul>';}
async function advance(){captureStep();if(scienceStep<4){scienceStep+=1;renderWizard();return;}const preview=await previewScience();const heavy=preview.estimate.runs*scienceDraft.ticks>20000||preview.estimate.decisionBytes>500*1024*1024||scienceDraft.recording==='detailed'&&scienceDraft.ticks>=1000;if(heavy&&!confirm('Серия большая: '+preview.estimate.runs+' прогонов. Журналы займут заметное место, срок — оценка, не обещание. Запустить?'))return;const started=await api('/api/science/start',requestBody());await refresh();scienceId=started.id;scienceView='study';scienceTab='overview';scienceStamp='';await window.scienceTick();}
async function openScience(id){scienceId=id;scienceView='study';scienceTab='overview';scienceStamp='';sciencePayload=await api('/api/science/study?id='+encodeURIComponent(id));renderStudy();}
function renderStudy(){
 const data=sciencePayload;if(!data)return;const series=data.series,study=data.study,paths=data.paths;const jobs=series.jobs,done=jobs.filter(job=>!['queued','running'].includes(job.status)).length;const active=jobs.find(job=>job.status==='running');
 const tabs=[['overview','Обзор'],['compare','Сравнение условий'],['stats','Подробная статистика'],['runs','Отдельные прогоны'],['files','Отчёт и файлы']];
 scienceRoot().innerHTML='<div class="page-title"><div><span class="eyebrow">ИССЛЕДОВАНИЕ</span><h1>'+esc(series.plan.science.title)+'</h1><p>'+esc(series.plan.science.hypothesis)+'</p></div><button type="button" id="science-back">К списку</button></div><div class="tabs">'+tabs.map(([id,label])=>'<button type="button" data-science-tab="'+id+'" class="'+(scienceTab===id?'active':'')+'">'+label+'</button>').join('')+'</div><div id="science-panel"></div>';
 document.getElementById('science-back').onclick=()=>{scienceView='list';renderList();};
 scienceRoot().querySelectorAll('[data-science-tab]').forEach(button=>button.onclick=()=>{scienceTab=button.dataset.scienceTab;renderStudy();});
 const panel=document.getElementById('science-panel');
 const progress='<div class="panel"><p>'+esc(series.plan.science.title)+'</p><p>Готово '+done+' из '+jobs.length+(active?' · сейчас '+esc(active.arm)+' seed '+esc(active.seed)+(active.progress?' · тик '+esc(active.progress.tick+' / '+active.progress.target):''):'')+'</p><progress max="'+jobs.length+'" value="'+done+'"></progress><p class="muted">'+esc(paths.absolute)+'</p><p class="muted">'+esc(active?.progress?.estimatedRemainingSeconds!=null?'Оценка остатка активного прогона: '+Math.round(active.progress.estimatedRemainingSeconds)+' с. Это не обещание срока.':'Оценки остатка пока нет.')+'</p></div>';
 if(!study){panel.innerHTML=progress+'<div class="empty"><h2>Отчёт появится после первого сохранённого итога</h2><p class="muted">Неуспешный прогон останется в списке и не будет заменён нулём.</p></div>';return;}
 if(scienceTab==='overview')panel.innerHTML=progress+'<div class="metric-grid"><div class="metric"><span>СТАТУС</span><strong style="font-size:18px">'+esc(study.status==='complete'?'Завершено':study.status==='preliminary'?'Предварительно':'Есть пропуски')+'</strong></div><div class="metric"><span>ПРОГОНЫ</span><strong>'+jobs.length+'</strong></div><div class="metric"><span>В СТАТИСТИКЕ</span><strong>'+study.runs.filter(run=>run.included).length+'</strong></div><div class="metric"><span>ОСНОВНОЙ ПОКАЗАТЕЛЬ</span><strong style="font-size:18px">'+esc(scienceMetric(study.experiment.primaryMetric)?.title||study.experiment.primaryMetric)+'</strong></div></div><h2>Главные результаты исследования</h2>'+study.findings.map(item=>'<p class="finding '+esc(item.kind)+'">'+esc(item.text)+(item.metricId?' <button type="button" data-jump="'+esc(item.metricId)+'">К таблице</button>':'')+'</p>').join('')+'<div class="button-row"><button type="button" id="science-open-report">Открыть отчёт</button><button type="button" id="science-zip">Скачать результаты ZIP</button><button type="button" id="science-analyze">Обновить расчёт</button></div>'+(study.limitations.length?'<ul>'+study.limitations.map(item=>'<li>'+esc(item)+'</li>').join('')+'</ul>':'');
 if(scienceTab==='compare')panel.innerHTML=comparePanel(study);
 if(scienceTab==='stats')panel.innerHTML=statsPanel(study);
 if(scienceTab==='runs')panel.innerHTML=runsPanel(series,study);
 if(scienceTab==='files')panel.innerHTML=filesPanel(paths,study);
 panel.querySelectorAll('[data-jump]').forEach(button=>button.onclick=()=>{scienceTab='stats';renderStudy();});
 const report=document.getElementById('science-open-report');if(report)report.onclick=()=>openScienceReport(scienceId).catch(alertError);
 const zip=document.getElementById('science-zip');if(zip)zip.onclick=()=>download('/science-zip/'+encodeURIComponent(scienceId)+'.zip','results.zip').catch(alertError);
 const again=document.getElementById('science-analyze');if(again)again.onclick=async()=>{sciencePayload.study=await api('/api/science/analyze',{id:scienceId,force:true});scienceStamp='';renderStudy();};
 bindScienceActions(series);
 drawScienceCharts(study);
}
function comparePanel(study){
 const metricId=scienceChart||study.experiment.primaryMetric;const metric=study.metrics[metricId];
 return '<label>Показатель<select id="science-chart-metric">'+Object.values(study.metrics).map(item=>'<option value="'+esc(item.id)+'" '+(item.id===metricId?'selected':'')+'>'+esc(item.title)+'</option>').join('')+'</select></label><div id="science-bars"></div><div id="science-lines"></div><h2>Различия групп</h2>'+table(['Группа','n','Среднее','Медиана','95% интервал среднего'],metric.groups.map(group=>[esc(group.label),group.n,esc(scienceNumber(group.mean,metric)),esc(scienceNumber(group.median,metric)),esc(group.interval?scienceNumber(group.interval[0],metric)+' … '+scienceNumber(group.interval[1],metric):'не рассчитан')]))+'<h2>Парные seeds</h2>'+table(['Сравнение','Пар','Среднее различие','95% интервал','Относительно, %','p'],(metric.paired||[]).map(item=>[esc(item.label),item.pairs,esc(scienceNumber(item.mean,metric)),esc(item.confidenceInterval?scienceNumber(item.confidenceInterval[0],metric)+' … '+scienceNumber(item.confidenceInterval[1],metric):'не рассчитан'),esc(item.relativeChangePercent==null?'не определено':Number(item.relativeChangePercent).toLocaleString('ru-RU',{maximumFractionDigits:2})),esc(item.pValue==null?'не рассчитано':Number(item.pValue).toLocaleString('ru-RU',{maximumFractionDigits:4}))]))+'<p class="muted">Парное различие — группа минус первая группа на том же seed. Линии ниже, если они есть, показывают отдельные миры, а не среднее этих миров. Ось столбцов начинается с нуля, когда все средние неотрицательные.</p>';
}
function statsPanel(study){
 return Object.values(study.metrics).map(metric=>'<section class="panel"><h2>'+esc(metric.title)+' · '+esc(metric.role==='primary'?'основная гипотеза':'разведочный показатель')+'</h2><p>'+esc(metric.definition)+'</p><p class="muted">Источник: '+esc(metric.source)+'. Единица: '+esc(metric.unitLabel)+'.</p>'+table(['Группа','n','Пропуски','Среднее','Медиана','Минимум','Максимум','Отклонение'],metric.groups.map(group=>[esc(group.label),group.n,group.missing,esc(scienceNumber(group.mean,metric)),esc(scienceNumber(group.median,metric)),esc(scienceNumber(group.minimum,metric)),esc(scienceNumber(group.maximum,metric)),esc(scienceNumber(group.standardDeviation,metric))]))+'<p class="muted">'+esc((groupWarnings(metric)).join(' '))+'</p></section>').join('')+seedTable(study);
}
function groupWarnings(metric){return [...new Set(metric.groups.flatMap(group=>group.warnings||[]).concat((metric.paired||[]).flatMap(item=>item.warnings||[])))];}
function seedTable(study){
 const metrics=Object.values(study.metrics);
 const header=['Прогон','Группа','Seed','Статус',...metrics.map(item=>item.title)];
 const rows=study.runs.map(run=>[esc(run.id),esc(run.group),esc(run.seed),esc(scienceStatus(run)),...metrics.map(item=>esc(scienceNumber(run.metrics[item.id],item)))]);
 return '<h2>Каждый seed</h2>'+table(header,rows);
}
function runsPanel(series,study){
 return table(['Прогон','Группа','Seed','Статус','Почему не в статистике',''],series.jobs.map(job=>{const row=study.runs.find(item=>item.id===job.id)||{};return [esc(job.id),esc(row.group||job.arm),esc(job.seed),'<span class="pill '+esc(job.status)+'">'+esc(scienceStatus(job))+'</span>',esc(row.exclusion||job.error||''),'<button type="button" data-open-run="'+esc(job.id)+'">Открыть прогон</button>'+(row.exclusion?' <button type="button" data-retry-run="'+esc(job.id)+'">Повторить</button>':'')];}))+'<button type="button" id="science-retry-all">Повторить все неуспешные</button>';
}
function filesPanel(paths,study){
 return '<div class="panel"><h2>Куда сохранено</h2><p>'+esc(paths.windowsNote)+'</p><div class="path-box" id="science-path">'+esc(paths.absolute)+'</div><p>Относительный путь: '+esc(paths.relative)+'</p><p>Отчёт: '+esc(paths.report)+'</p><p>Сформирован: '+esc(study.generatedAt)+' (Самара)</p><div class="button-row"><button type="button" id="science-open-folder">Открыть папку</button><button type="button" id="science-copy">Скопировать путь</button><button type="button" id="science-open-report">Открыть отчёт</button><button type="button" id="science-zip">Скачать результаты ZIP</button></div></div>'+table(['Файл','Назначение','Размер'],(study.files||[]).map(file=>[esc(file.name),esc(file.role),bytes(file.bytes||0)]));
}
function bindScienceActions(){
 scienceRoot().querySelectorAll('[data-open-run]').forEach(button=>button.onclick=()=>window.openStudy(scienceId,button.dataset.openRun));
 scienceRoot().querySelectorAll('[data-retry-run]').forEach(button=>button.onclick=()=>api('/api/science/retry',{id:scienceId,job:button.dataset.retryRun}).then(refresh).catch(alertError));
 const all=document.getElementById('science-retry-all');if(all)all.onclick=()=>api('/api/science/retry',{id:scienceId}).then(refresh).catch(alertError);
 const folder=document.getElementById('science-open-folder');if(folder)folder.onclick=async()=>{const result=await api('/api/science/open',{id:scienceId});notice(result.opened?'Папка открыта.':result.note+' '+result.path);};
 const copy=document.getElementById('science-copy');if(copy)copy.onclick=async()=>{const text=document.getElementById('science-path').textContent;try{await navigator.clipboard.writeText(text);notice('Путь скопирован.');}catch(e){notice(text);}};
 const metric=document.getElementById('science-chart-metric');if(metric)metric.onchange=()=>{scienceChart=metric.value;renderStudy();};
}
function drawScienceCharts(study){
 const bars=document.getElementById('science-bars');if(!bars)return;const metric=study.metrics[scienceChart||study.experiment.primaryMetric];
 const canvas=document.createElement('canvas');canvas.width=900;canvas.height=280;canvas.setAttribute('aria-label',metric.title);bars.appendChild(canvas);
 const context=canvas.getContext('2d'),groups=metric.groups.filter(group=>group.mean!=null);context.fillStyle='#1c1f27';context.fillRect(0,0,900,280);context.fillStyle='#ebe7df';context.font='14px sans-serif';context.fillText(metric.title+' · средние независимых миров · '+metric.unitLabel+' · n на столбце',16,24);
 if(!groups.length){context.fillText('Нет чисел для столбцов',16,80);return;}
 const nums=groups.flatMap(group=>[Number(group.mean),...(group.interval||[]).map(Number)]);
 let low=Math.min(...nums,0),high=Math.max(...nums,0);if(!groups.every(group=>Number(group.mean)>=0))low=Math.min(...nums);else low=0;if(high===low)high+=1;
 groups.forEach((group,index)=>{const x=80+index*140;const y=value=>250-(Number(value)-low)/(high-low)*190;context.fillStyle='#d4b483';const top=y(group.mean),base=y(0);context.fillRect(x,Math.min(top,base),48,Math.max(2,Math.abs(base-top)));if(group.interval){context.strokeStyle='#ebe7df';context.beginPath();context.moveTo(x+24,y(group.interval[0]));context.lineTo(x+24,y(group.interval[1]));context.stroke();}context.fillStyle='#a39e94';context.fillText(group.label+' n='+group.n,x,268);});
 context.fillStyle='#a39e94';context.fillText(low===0?'Ось начинается с нуля.':'Есть отрицательные значения, ось не обрезана до нуля.',16,44);
 const lines=document.getElementById('science-lines');
 api('/api/science/chart?id='+encodeURIComponent(scienceId)+'&metric='+encodeURIComponent(metric.id)).then(chart=>{
  if(!chart.trajectories?.length){lines.innerHTML='<p class="muted">Хода по тикам у этого показателя нет. Столбцы выше сравнивают независимые миры.</p>';return;}
  const board=document.createElement('canvas');board.width=900;board.height=280;board.setAttribute('aria-label','Ход '+metric.title);lines.appendChild(board);const pen=board.getContext('2d');pen.fillStyle='#1c1f27';pen.fillRect(0,0,900,280);pen.fillStyle='#ebe7df';pen.fillText('Отдельные миры · '+metric.title+' · ось X тики партии · '+metric.unitLabel,16,24);
  const points=chart.trajectories.flatMap(run=>run.points);const xs=points.map(point=>point.tick),ys=points.map(point=>Number(point.value));let y0=Math.min(...ys,0),y1=Math.max(...ys,0);if(Math.min(...ys)>=0)y0=0;if(y1===y0)y1+=1;const x0=Math.min(...xs),x1=Math.max(...xs)||1;const colors=['#d4b483','#9db8a4','#efa29a','#8fb4d6','#c7b2d6'];
  chart.trajectories.forEach((run,index)=>{pen.strokeStyle=colors[index%colors.length];pen.beginPath();run.points.forEach((point,at)=>{const x=40+(point.tick-x0)/(x1-x0||1)*840,y=250-(Number(point.value)-y0)/(y1-y0)*190;if(at)pen.lineTo(x,y);else pen.moveTo(x,y);});pen.stroke();});
  lines.insertAdjacentHTML('beforeend','<p class="muted">Каждая линия — один мир ('+chart.trajectories.map(run=>run.group+' seed '+run.seed).join(', ')+'). Это не среднее независимых миров. '+(y0===0?'Ось значений начинается с нуля.':'Ось значений включает отрицательные числа и не обрезана.')+'</p>');
 }).catch(()=>{lines.innerHTML='<p class="muted">Ход по тикам появится, когда в отчёте будут срезы обычной или подробной записи.</p>';});
}
async function openScienceReport(id){
 const popup=window.open('','delsk-report');
 if(!popup)throw Error('Браузер не открыл окно отчёта. Файл report.html лежит в папке, путь которой показан на этой странице.');
 const response=await fetch('/api/science/report?id='+encodeURIComponent(id),{headers:{'X-Lab-Token':token}});
 if(!response.ok){popup.close();const error=await response.json();throw Error(error.error||response.status);}
 popup.document.open();popup.document.write(await response.text());popup.document.close();
}
